using Banking.Application.Common.Interfaces;
using Banking.Application.Transfers.Commands.Interbank;
using Banking.Application.Transfers.Commands.ProcessOutbox;
using Banking.Application.Transfers.Commands.Reconcile;
using Banking.Domain.Accounts;
using Banking.Domain.Outbox;
using Banking.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Banking.Application.Tests;

/// <summary>
/// Kiểm chứng luồng liên ngân hàng (Giai đoạn 2): saga qua suspense + outbox,
/// bank ngoài thành công → Posted, thất bại → Reversed (hoàn tiền), timeout → reconcile giải quyết.
/// Tiền không bao giờ nằm nửa vời; sổ ghi kép luôn cân; tổng tiền (gồm suspense + external_out) bảo toàn.
/// </summary>
public class InterbankSagaTests
{
    private static async Task<InterbankTransferResult> StartInterbank(BankDbFixture db, string key, decimal amount)
    {
        using var ctx = db.NewContext();
        return await new InterbankTransferCommandHandler(ctx)
            .Handle(new InterbankTransferCommand(key, db.AliceId, "Techcombank", amount), CancellationToken.None);
    }

    private static async Task<int> ProcessOutbox(BankDbFixture db, ExternalResult send, ExternalResult? query = null)
    {
        using var ctx = db.NewContext();
        return await new ProcessOutboxCommandHandler(ctx, new FakeExternalBankGateway(send, query))
            .Handle(new ProcessOutboxCommand(), CancellationToken.None);
    }

    private static async Task<int> Reconcile(BankDbFixture db, ExternalResult query)
    {
        using var ctx = db.NewContext();
        // send không quan trọng ở reconcile; chỉ QueryStatusAsync được gọi.
        return await new ReconcileCommandHandler(ctx, new FakeExternalBankGateway(ExternalResult.Unknown, query))
            .Handle(new ReconcileCommand(), CancellationToken.None);
    }

    private static async Task<decimal> Bal(BankDbFixture db, Guid id)
    {
        using var ck = db.NewContext();
        return (await ck.Accounts.FindAsync(id))!.Balance;
    }

    // ---- Bước 1: tiền rời nguồn vào suspense, transfer Pending, có outbox ----
    [Fact]
    public async Task Buoc_1_ghi_no_nguon_vao_suspense_va_tao_outbox()
    {
        using var db = new BankDbFixture(1000m);

        var res = await StartInterbank(db, "ib-1", 100m);

        Assert.Equal("Pending", res.Status);
        Assert.Equal(900m, await Bal(db, db.AliceId));
        Assert.Equal(100m, await Bal(db, WellKnownAccounts.SuspenseId)); // tiền đang treo
        using var ck = db.NewContext();
        Assert.Equal(1, await ck.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending));
        Assert.Equal(TransferStatus.Pending, (await ck.Transfers.FirstAsync()).Status);
    }

    // ---- Saga THÀNH CÔNG → Posted, tiền ra EXTERNAL_OUT, suspense về 0 ----
    [Fact]
    public async Task Bank_ngoai_thanh_cong_thi_Posted_va_tien_ra_ngoai()
    {
        using var db = new BankDbFixture(1000m);
        var res = await StartInterbank(db, "ib-1", 100m);

        var handled = await ProcessOutbox(db, ExternalResult.Succeeded);

        Assert.Equal(1, handled);
        Assert.Equal(900m, await Bal(db, db.AliceId));
        Assert.Equal(0m, await Bal(db, WellKnownAccounts.SuspenseId));      // hết treo
        Assert.Equal(100m, await Bal(db, WellKnownAccounts.ExternalOutId)); // tiền đã ra ngoài

        using var ck = db.NewContext();
        Assert.Equal(TransferStatus.Completed, (await ck.Transfers.FindAsync(res.TransferId))!.Status);
        Assert.Equal(1, await ck.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Processed));
        await AssertConservationAndBalancedLedger(db);
    }

    // ---- Saga THẤT BẠI → Reversed, hoàn tiền về nguồn, suspense về 0 ----
    [Fact]
    public async Task Bank_ngoai_tu_choi_thi_Reversed_va_hoan_tien()
    {
        using var db = new BankDbFixture(1000m);
        var res = await StartInterbank(db, "ib-1", 100m);

        var handled = await ProcessOutbox(db, ExternalResult.Failed);

        Assert.Equal(1, handled);
        Assert.Equal(1000m, await Bal(db, db.AliceId));                    // được hoàn đủ
        Assert.Equal(0m, await Bal(db, WellKnownAccounts.SuspenseId));
        Assert.Equal(0m, await Bal(db, WellKnownAccounts.ExternalOutId));  // không có tiền ra ngoài

        using var ck = db.NewContext();
        Assert.Equal(TransferStatus.Reversed, (await ck.Transfers.FindAsync(res.TransferId))!.Status);
        await AssertConservationAndBalancedLedger(db);
    }

    // ---- Timeout (Unknown) → giữ Pending, KHÔNG đoán; reconcile giải quyết ----
    [Fact]
    public async Task Timeout_thi_giu_Pending_roi_reconcile_giai_quyet()
    {
        using var db = new BankDbFixture(1000m);
        var res = await StartInterbank(db, "ib-1", 100m);

        var handled = await ProcessOutbox(db, ExternalResult.Unknown);
        Assert.Equal(0, handled); // chưa giải quyết
        Assert.Equal(100m, await Bal(db, WellKnownAccounts.SuspenseId)); // vẫn treo, tiền không bốc hơi
        using (var ck = db.NewContext())
            Assert.Equal(TransferStatus.Pending, (await ck.Transfers.FindAsync(res.TransferId))!.Status);

        // Đối soát: tra lại thấy Succeeded → settle.
        var resolved = await Reconcile(db, ExternalResult.Succeeded);
        Assert.Equal(1, resolved);
        Assert.Equal(0m, await Bal(db, WellKnownAccounts.SuspenseId));
        Assert.Equal(100m, await Bal(db, WellKnownAccounts.ExternalOutId));
        using (var ck = db.NewContext())
            Assert.Equal(TransferStatus.Completed, (await ck.Transfers.FindAsync(res.TransferId))!.Status);
        await AssertConservationAndBalancedLedger(db);
    }

    // ---- Outbox xử lý idempotent: chạy 2 lần không settle 2 lần ----
    [Fact]
    public async Task Outbox_xu_ly_lai_khong_settle_hai_lan()
    {
        using var db = new BankDbFixture(1000m);
        await StartInterbank(db, "ib-1", 100m);

        await ProcessOutbox(db, ExternalResult.Succeeded);
        var handledAgain = await ProcessOutbox(db, ExternalResult.Succeeded); // chạy lại

        Assert.Equal(0, handledAgain); // không còn gì Pending để xử lý
        Assert.Equal(900m, await Bal(db, db.AliceId));
        Assert.Equal(100m, await Bal(db, WellKnownAccounts.ExternalOutId)); // vẫn 100, không nhân đôi
        using var ck = db.NewContext();
        Assert.Equal(4, await ck.LedgerEntries.CountAsync()); // đúng 4 bút toán (bước 1: 2, settle: 2), không sinh thêm
    }

    // ---- Idempotency Giai đoạn 1 vẫn giữ: gọi lại interbank cùng key → cache ----
    [Fact]
    public async Task Interbank_retry_cung_key_tra_cache()
    {
        using var db = new BankDbFixture(1000m);
        var first = await StartInterbank(db, "ib-1", 100m);
        var second = await StartInterbank(db, "ib-1", 100m);

        Assert.False(first.ServedFromCache);
        Assert.True(second.ServedFromCache);
        Assert.Equal(first.TransferId, second.TransferId);
        Assert.Equal(900m, await Bal(db, db.AliceId)); // chỉ trừ 1 lần
    }

    // Bất biến: mọi bút toán cân (tổng hiệu ứng = 0) + tổng tiền toàn hệ thống (gồm suspense/external) = 1000.
    private static async Task AssertConservationAndBalancedLedger(BankDbFixture db)
    {
        using var ck = db.NewContext();
        var entries = await ck.LedgerEntries.ToListAsync();
        Assert.Equal(0m, entries.Sum(e => e.SignedEffect)); // sổ luôn cân

        var total = await ck.Accounts.SumAsync(a => a.Balance);
        Assert.Equal(1000m, total); // bảo toàn (Alice 1000 ban đầu; tiền chỉ dịch chuyển giữa các tài khoản)
    }
}
