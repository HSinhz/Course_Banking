using Banking.Application.Common.Exceptions;
using Banking.Application.Transfers.Commands.TransferMoney;
using Banking.Domain.Idempotency;
using Banking.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Banking.Application.Tests;

/// <summary>
/// Kiểm chứng chuyển tiền theo mô hình core banking Giai đoạn 1:
/// khách bấm chuyển 1 lần KHÔNG bao giờ bị trừ 2 lần (retry / double-click / đồng thời),
/// double-entry ledger bảo toàn tiền, và optimistic concurrency chặn đua số dư khác key.
/// </summary>
public class TransferMoneyIdempotencyTests
{
    // Mỗi "request" chạy trên 1 DbContext mới (giống 1 HTTP request/scope thật).
    private static async Task<TransferMoneyResult> Send(BankDbFixture db, TransferMoneyCommand cmd)
    {
        using var ctx = db.NewContext();
        var handler = new TransferMoneyCommandHandler(ctx);
        return await handler.Handle(cmd, CancellationToken.None);
    }

    // ---- CASE 1: chuyển hợp lệ → trừ đúng 1 lần + sinh cặp bút toán ----
    [Fact]
    public async Task Transfer_hop_le_thi_tru_tien_dung_mot_lan()
    {
        using var db = new BankDbFixture(aliceBalance: 1000m);

        var res = await Send(db, new TransferMoneyCommand("key-1", db.AliceId, db.BobId, 100m));

        Assert.Equal("Completed", res.Status);
        Assert.False(res.ServedFromCache);

        using var ck = db.NewContext();
        Assert.Equal(900m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance);
        Assert.Equal(100m, (await ck.Accounts.FindAsync(db.BobId))!.Balance);
        Assert.Equal(1, await ck.Transfers.CountAsync());
        Assert.Equal(2, await ck.LedgerEntries.CountAsync()); // 1 Debit + 1 Credit
    }

    // ---- CASE 2: retry cùng Idempotency-Key → trả response CŨ, KHÔNG trừ lần 2 ----
    [Fact]
    public async Task Retry_cung_key_thi_tra_ket_qua_cu_va_khong_tru_lan_hai()
    {
        using var db = new BankDbFixture(1000m);
        var cmd = new TransferMoneyCommand("key-1", db.AliceId, db.BobId, 100m);

        var first = await Send(db, cmd);
        var second = await Send(db, cmd); // client bấm/gửi lại cùng key

        Assert.False(first.ServedFromCache);
        Assert.True(second.ServedFromCache);
        Assert.Equal(first.TransferId, second.TransferId); // cùng một giao dịch

        using var ck = db.NewContext();
        Assert.Equal(900m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance); // vẫn chỉ trừ 1 lần
        Assert.Equal(1, await ck.Transfers.CountAsync());
        Assert.Equal(2, await ck.LedgerEntries.CountAsync()); // không sinh thêm bút toán
    }

    // ---- CASE 3: thiếu Idempotency-Key → từ chối ----
    [Fact]
    public async Task Thieu_key_thi_bi_tu_choi()
    {
        using var db = new BankDbFixture(1000m);

        await Assert.ThrowsAsync<MissingIdempotencyKeyException>(() =>
            Send(db, new TransferMoneyCommand("", db.AliceId, db.BobId, 100m)));

        using var ck = db.NewContext();
        Assert.Equal(1000m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance);
        Assert.Equal(0, await ck.Transfers.CountAsync());
    }

    // ---- CASE 4 (NGỮ NGHĨA MỚI): bản ghi InProgress sót lại (crash cũ) → retry cùng key THU HỒI & chuyển thành công ----
    // Trước đây trả 409 vĩnh viễn (key kẹt). Thiết kế 1-transaction làm bug này biến mất: InProgress không còn hợp lệ.
    [Fact]
    public async Task Ban_ghi_InProgress_sot_lai_thi_duoc_thu_hoi_va_chuyen_thanh_cong()
    {
        using var db = new BankDbFixture(1000m);

        // Mô phỏng một claim cũ bị kẹt (crash trước khi hoàn tất).
        using (var seed = db.NewContext())
        {
            seed.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = "key-1",
                Status = IdempotencyStatus.InProgress
            });
            await seed.SaveChangesAsync();
        }

        var res = await Send(db, new TransferMoneyCommand("key-1", db.AliceId, db.BobId, 100m));

        Assert.Equal("Completed", res.Status);
        Assert.False(res.ServedFromCache);

        using var ck = db.NewContext();
        Assert.Equal(900m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance); // đã chuyển được
        Assert.Equal(1, await ck.Transfers.CountAsync());
        Assert.Equal(IdempotencyStatus.Completed,
            (await ck.IdempotencyRecords.FirstAsync(r => r.Key == "key-1")).Status);
    }

    // ---- CASE 5 (race thật): N request đồng thời cùng key → chỉ trừ 1 lần, kẻ thua trả cache (không còn 409/null) ----
    [Fact]
    public async Task Nhieu_request_dong_thoi_cung_key_chi_tru_dung_mot_lan()
    {
        using var db = new BankDbFixture(1000m);
        var cmd = new TransferMoneyCommand("race", db.AliceId, db.BobId, 100m);

        const int n = 6;
        var tasks = Enumerable.Range(0, n).Select(_ => Task.Run(() => Send(db, cmd))).ToArray();
        var results = await Task.WhenAll(tasks);

        using var ck = db.NewContext();
        Assert.Equal(900m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance); // trừ đúng 1 lần
        Assert.Equal(1, await ck.Transfers.CountAsync());                       // đúng 1 giao dịch
        Assert.Equal(2, await ck.LedgerEntries.CountAsync());                   // đúng 1 cặp bút toán

        // Đúng 1 request thực thi thật; các request còn lại đều trả cache (không request nào bị null).
        Assert.All(results, r => Assert.NotNull(r));
        Assert.Equal(1, results.Count(r => !r.ServedFromCache));
        Assert.Equal(n - 1, results.Count(r => r.ServedFromCache));
    }

    // ---- CASE 6: các key KHÁC nhau → là các giao dịch độc lập, đều thực hiện ----
    [Fact]
    public async Task Key_khac_nhau_thi_deu_thuc_hien()
    {
        using var db = new BankDbFixture(1000m);

        await Send(db, new TransferMoneyCommand("key-1", db.AliceId, db.BobId, 100m));
        await Send(db, new TransferMoneyCommand("key-2", db.AliceId, db.BobId, 200m));

        using var ck = db.NewContext();
        Assert.Equal(700m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance);
        Assert.Equal(300m, (await ck.Accounts.FindAsync(db.BobId))!.Balance);
        Assert.Equal(2, await ck.Transfers.CountAsync());
        Assert.Equal(4, await ck.LedgerEntries.CountAsync());
    }

    // ---- CASE 7: số dư không đủ → không trừ tiền, không tạo giao dịch, KEY KHÔNG BỊ KẸT ----
    [Fact]
    public async Task So_du_khong_du_thi_khong_tru_tien()
    {
        using var db = new BankDbFixture(aliceBalance: 50m);

        await Assert.ThrowsAsync<InsufficientFundsException>(() =>
            Send(db, new TransferMoneyCommand("key-1", db.AliceId, db.BobId, 100m)));

        using var ck = db.NewContext();
        Assert.Equal(50m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance);
        Assert.Equal(0m, (await ck.Accounts.FindAsync(db.BobId))!.Balance);
        Assert.Equal(0, await ck.Transfers.CountAsync());
        Assert.Equal(0, await ck.LedgerEntries.CountAsync());
        Assert.Equal(0, await ck.IdempotencyRecords.CountAsync()); // key KHÔNG bị claim
    }

    // ---- T-Ledger-Conservation: sổ ghi kép bảo toàn tiền ----
    [Fact]
    public async Task Ledger_ghi_kep_bao_toan_tien()
    {
        using var db = new BankDbFixture(1000m);

        await Send(db, new TransferMoneyCommand("key-1", db.AliceId, db.BobId, 250m));

        using var ck = db.NewContext();
        var entries = await ck.LedgerEntries.ToListAsync();

        // Đúng 1 Debit + 1 Credit, tổng Debit = tổng Credit.
        Assert.Equal(1, entries.Count(e => e.Direction == LedgerDirection.Debit));
        Assert.Equal(1, entries.Count(e => e.Direction == LedgerDirection.Credit));
        Assert.Equal(entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount),
                     entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount));

        // Tổng hiệu ứng có dấu trên toàn sổ = 0 (tiền không sinh ra/mất đi).
        Assert.Equal(0m, entries.Sum(e => e.SignedEffect));

        // Account.Balance khớp SUM hiệu ứng ledger của chính nó (số dư duy trì == số dư suy ra).
        var alice = await ck.Accounts.FindAsync(db.AliceId);
        var bob = await ck.Accounts.FindAsync(db.BobId);
        Assert.Equal(1000m + entries.Where(e => e.AccountId == db.AliceId).Sum(e => e.SignedEffect), alice!.Balance);
        Assert.Equal(0m + entries.Where(e => e.AccountId == db.BobId).Sum(e => e.SignedEffect), bob!.Balance);
    }

    // ---- T-Balance-Race: 2 lệnh KHÁC key đồng thời trên cùng tài khoản → optimistic concurrency chặn âm tiền ----
    [Fact]
    public async Task Hai_lenh_khac_key_dong_thoi_khong_lam_am_tien()
    {
        using var db = new BankDbFixture(aliceBalance: 150m); // chỉ đủ cho 1 lệnh 100

        var t1 = Task.Run(async () =>
        {
            try { await Send(db, new TransferMoneyCommand("key-A", db.AliceId, db.BobId, 100m)); return (ok: true, insufficient: false); }
            catch (InsufficientFundsException) { return (ok: false, insufficient: true); }
        });
        var t2 = Task.Run(async () =>
        {
            try { await Send(db, new TransferMoneyCommand("key-B", db.AliceId, db.BobId, 100m)); return (ok: true, insufficient: false); }
            catch (InsufficientFundsException) { return (ok: false, insufficient: true); }
        });
        var r = await Task.WhenAll(t1, t2);

        // Đúng 1 lệnh thành công, đúng 1 lệnh bị chặn vì số dư không đủ (không có chuyện cả hai cùng trừ).
        Assert.Equal(1, r.Count(x => x.ok));
        Assert.Equal(1, r.Count(x => x.insufficient));

        using var ck = db.NewContext();
        Assert.Equal(50m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance); // KHÔNG âm
        Assert.Equal(100m, (await ck.Accounts.FindAsync(db.BobId))!.Balance);
        Assert.Equal(1, await ck.Transfers.CountAsync());
        Assert.Equal(2, await ck.LedgerEntries.CountAsync());
    }

    // ---- T-StuckKey-Recovered: lỗi số dư không làm key kẹt → nạp tiền rồi retry cùng key thành công ----
    [Fact]
    public async Task Loi_so_du_khong_lam_key_ket_retry_cung_key_thanh_cong()
    {
        using var db = new BankDbFixture(aliceBalance: 50m);
        var cmd = new TransferMoneyCommand("key-1", db.AliceId, db.BobId, 100m);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => Send(db, cmd));

        // Nạp thêm tiền cho Alice.
        using (var top = db.NewContext())
        {
            var alice = await top.Accounts.FindAsync(db.AliceId);
            alice!.Balance = 200m;
            await top.SaveChangesAsync();
        }

        // Retry CÙNG key → thành công (key không hề bị kẹt).
        var res = await Send(db, cmd);
        Assert.Equal("Completed", res.Status);
        Assert.False(res.ServedFromCache);

        using var ck = db.NewContext();
        Assert.Equal(100m, (await ck.Accounts.FindAsync(db.AliceId))!.Balance);
        Assert.Equal(1, await ck.Transfers.CountAsync());
    }
}
