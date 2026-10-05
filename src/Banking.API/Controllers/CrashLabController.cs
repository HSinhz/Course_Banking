using System.Text.Json;
using Banking.Domain.Idempotency;
using Banking.Domain.Ledger;
using Banking.Domain.Transfers;
using Banking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Banking.API.Controllers;

/// <summary>
/// LAB GIÁO DỤC — mô phỏng "crash giữa transaction" cho chuyển tiền NỘI BỘ bằng DB THẬT.
/// Cố tình mở một transaction thật (BEGIN), trừ/cộng tiền + INSERT key, rồi:
///   - AppCrash / DbCrash → ROLLBACK (mô phỏng chưa kịp COMMIT) ⇒ DB tự huỷ, KHÔNG gì được ghi.
///   - Commit            → COMMIT (đối chứng)                    ⇒ tiền chuyển + key ghi bền vững.
/// Trong lúc transaction còn mở, đọc lại số dư từ MỘT KẾT NỐI ĐỘC LẬP để chứng minh
/// cách ly (MVCC): thế giới bên ngoài không hề thấy dữ liệu dở dang.
/// ⚠ KHÔNG dùng cho production — đây là công cụ minh hoạ.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/crash-lab")]
public class CrashLabController(IServiceScopeFactory scopeFactory) : ControllerBase
{
    public record SimulateRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, string Scenario);

    public record Snapshot(decimal FromBalance, decimal ToBalance, int TransferCount, bool KeyExists);
    public record StepLog(string Label, string Detail, bool Ok);
    public record SimulateResult(
        string Scenario, string ScenarioTitle, string IdempotencyKey, decimal Amount,
        string FromName, string ToName,
        Snapshot Before, Snapshot During, Snapshot After,
        bool Committed, bool MoneyMoved, bool KeyWritten, bool RetrySafe,
        string Verdict, List<StepLog> Steps);

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromBody] SimulateRequest body, CancellationToken ct)
    {
        var scenario = (body.Scenario ?? string.Empty).Trim();
        if (scenario is not ("AppCrash" or "DbCrash" or "Commit"))
            return BadRequest(new { error = "Scenario phải là AppCrash | DbCrash | Commit." });
        if (body.FromAccountId == body.ToAccountId)
            return BadRequest(new { error = "Nguồn và đích phải khác nhau." });
        if (body.Amount <= 0)
            return BadRequest(new { error = "Số tiền phải > 0." });

        // Key sinh sẵn cho lệnh mô phỏng — dùng để chứng minh "đã ghi vào DB chưa".
        var key = Guid.NewGuid().ToString();
        var steps = new List<StepLog>();

        // Đọc snapshot trên MỘT kết nối ĐỘC LẬP (scope riêng ⇒ DbContext riêng ⇒ connection riêng).
        async Task<Snapshot> ReadSnapshot()
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            var f = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == body.FromAccountId, ct);
            var t = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == body.ToAccountId, ct);
            var count = await db.Transfers.CountAsync(ct);
            var keyExists = await db.IdempotencyRecords.AnyAsync(r => r.Key == key, ct);
            return new Snapshot(f.Balance, t.Balance, count, keyExists);
        }

        // Validate tồn tại + đủ tiền, đồng thời lấy tên tài khoản để hiển thị.
        string fromName, toName;
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            var f = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == body.FromAccountId, ct);
            var t = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == body.ToAccountId, ct);
            if (f is null || t is null) return NotFound(new { error = "Tài khoản không tồn tại." });
            if (f.Balance < body.Amount) return UnprocessableEntity(new { error = "Số dư không đủ cho mô phỏng." });
            fromName = f.Name; toName = t.Name;
        }

        var before = await ReadSnapshot();
        steps.Add(new StepLog("① BEFORE — chụp trạng thái",
            $"{fromName}={before.FromBalance:0.##}, {toName}={before.ToBalance:0.##}; tổng Transfers={before.TransferCount}; key tồn tại={before.KeyExists}.", true));

        // ---- Mở transaction THẬT trên scope làm việc ----
        using var workScope = scopeFactory.CreateScope();
        var work = workScope.ServiceProvider.GetRequiredService<BankingDbContext>();
        await using var tx = await work.Database.BeginTransactionAsync(ct);
        steps.Add(new StepLog("② BEGIN", "Mở transaction DB — mọi thay đổi sau đây chỉ là 'nháp' cho tới khi COMMIT.", true));

        var from = await work.Accounts.FirstAsync(a => a.Id == body.FromAccountId, ct);
        var to = await work.Accounts.FirstAsync(a => a.Id == body.ToAccountId, ct);
        from.Balance -= body.Amount; from.Version++;   // EF: UPDATE ... WHERE Version=@old
        to.Balance += body.Amount; to.Version++;

        var transfer = new Transfer
        {
            IdempotencyKey = key,
            FromAccountId = from.Id,
            ToAccountId = to.Id,
            Amount = body.Amount,
            Status = TransferStatus.Completed
        };
        work.Transfers.Add(transfer);
        work.LedgerEntries.Add(new LedgerEntry
        {
            TransferId = transfer.Id, AccountId = from.Id,
            Direction = LedgerDirection.Debit, Amount = body.Amount, BalanceAfter = from.Balance
        });
        work.LedgerEntries.Add(new LedgerEntry
        {
            TransferId = transfer.Id, AccountId = to.Id,
            Direction = LedgerDirection.Credit, Amount = body.Amount, BalanceAfter = to.Balance
        });
        var payload = new { transferId = transfer.Id, fromBalanceAfter = from.Balance, status = "Completed" };
        work.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key, Status = IdempotencyStatus.Completed, ResponseBody = JsonSerializer.Serialize(payload)
        });

        // INSERT/UPDATE được gửi xuống DB nhưng nằm TRONG transaction — CHƯA có commit record.
        await work.SaveChangesAsync(ct);
        steps.Add(new StepLog("③ WRITE (chưa COMMIT)",
            $"Đã trừ {fromName}, cộng {toName}, tạo Transfer + 2 bút toán + INSERT key — tất cả treo trong transaction chưa chốt.", true));

        // ---- DURING: đọc từ kết nối KHÁC để chứng minh cách ly (MVCC / READ COMMITTED) ----
        var during = await ReadSnapshot();
        var isolatedOk = during.FromBalance == before.FromBalance && !during.KeyExists;
        steps.Add(new StepLog("④ DURING — nhìn từ kết nối khác",
            $"Bên ngoài vẫn thấy {fromName}={during.FromBalance:0.##} (số CŨ), key tồn tại={during.KeyExists}. ⇒ Không ai đọc được dữ liệu dở dang.", isolatedOk));

        bool committed;
        string title;
        switch (scenario)
        {
            case "Commit":
                await tx.CommitAsync(ct);
                committed = true;
                title = "Đối chứng · COMMIT thành công";
                steps.Add(new StepLog("⑤ COMMIT", "Ghi commit record vào WAL và flush xuống đĩa ⇒ dữ liệu bền vững (ACID · Durability).", true));
                break;
            case "DbCrash":
                await tx.RollbackAsync(ct);   // WAL chưa có commit record → crash-recovery loại bỏ
                committed = false;
                title = "2B · DB chết trước COMMIT";
                steps.Add(new StepLog("⑤ CRASH (DB) → ROLLBACK",
                    "DB sập khi WAL CHƯA có commit record. Lúc khởi động lại, crash-recovery loại bỏ transaction chưa commit ⇒ như chưa từng xảy ra.", true));
                break;
            default: // AppCrash
                await tx.RollbackAsync(ct);   // app chết → kết nối đứt → DB tự ABORT
                committed = false;
                title = "2A · App chết trước COMMIT";
                steps.Add(new StepLog("⑤ CRASH (App) → ROLLBACK",
                    "Tiến trình app chết, kết nối TCP đứt ⇒ DB tự ABORT transaction chưa commit, nhả khoá.", true));
                break;
        }

        var after = await ReadSnapshot();
        var moneyMoved = after.FromBalance != before.FromBalance;
        var keyWritten = after.KeyExists;
        var retrySafe = !keyWritten;
        steps.Add(new StepLog("⑥ AFTER — chụp lại",
            $"{fromName}={after.FromBalance:0.##}, {toName}={after.ToBalance:0.##}; tổng Transfers={after.TransferCount}; key tồn tại={after.KeyExists}.", true));

        var verdict = committed
            ? $"✅ ĐÃ COMMIT: {fromName} giảm {before.FromBalance - after.FromBalance:0.##}, key đã ghi ⇒ retry cùng key sẽ trả CACHE (không trừ lại)."
            : $"✅ ROLLBACK: số dư về nguyên ({fromName}={after.FromBalance:0.##}), KHÔNG có key, KHÔNG có transfer mới ⇒ như chưa từng xảy ra, retry cùng key AN TOÀN.";

        return Ok(new SimulateResult(
            scenario, title, key, body.Amount, fromName, toName,
            before, during, after,
            committed, moneyMoved, keyWritten, retrySafe, verdict, steps));
    }
}
