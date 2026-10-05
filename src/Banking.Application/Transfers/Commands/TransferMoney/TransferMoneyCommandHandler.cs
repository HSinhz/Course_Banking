using System.Text.Json;
using Banking.Application.Common.Exceptions;
using Banking.Application.Common.Interfaces;
using Banking.Domain.Idempotency;
using Banking.Domain.Ledger;
using Banking.Domain.Transfers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Transfers.Commands.TransferMoney;

/// <summary>
/// Chuyển tiền nội bộ theo mô hình core banking (Giai đoạn 1):
/// - Double-entry ledger bất biến (Debit nguồn + Credit đích) ghi cùng transaction với số dư.
/// - Chống đua số dư (lost update) bằng optimistic concurrency token (Account.Version) + retry.
/// - Idempotency: claim key + business logic + đóng dấu Completed gói trong MỘT transaction
///   ⇒ bản ghi idempotency chỉ tồn tại ở trạng thái Completed; lỗi → rollback nhả key (không kẹt InProgress).
/// </summary>
public class TransferMoneyCommandHandler(IBankingDbContext db)
    : IRequestHandler<TransferMoneyCommand, TransferMoneyResult>
{
    private const int MaxRetries = 3;
    private readonly IBankingDbContext _db = db;

    public async Task<TransferMoneyResult> Handle(TransferMoneyCommand request, CancellationToken ct)
    {
        // Case: thiếu key → không cho đi tiếp (server không tin client).
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new MissingIdempotencyKeyException();

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            _db.ChangeTracker.Clear();

            // Fast-path: nếu key đã Completed → trả cache. Completed là trạng thái TERMINAL/bất biến
            // nên đọc ở đây an toàn (KHÔNG phải check-then-act khi tạo mới — việc tạo vẫn do UNIQUE INSERT gác).
            var existing = await _db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == request.IdempotencyKey, ct);
            if (existing is not null)
            {
                if (existing.Status == IdempotencyStatus.Completed && existing.ResponseBody is not null)
                {
                    var cached = JsonSerializer.Deserialize<TransferMoneyResult>(existing.ResponseBody)!;
                    return cached with { ServedFromCache = true };
                }

                // Bản ghi non-terminal còn sót (crash cũ / seed). Giai đoạn 1 single-transaction KHÔNG bao giờ
                // commit InProgress hợp lệ → coi đây là rác và thu hồi để nhả key. (Giai đoạn 2: dùng lease/TTL.)
                _db.IdempotencyRecords.Remove(
                    await _db.IdempotencyRecords.FirstAsync(r => r.Key == request.IdempotencyKey, ct));
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
            }

            var from = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId, ct)
                       ?? throw new AccountNotFoundException(request.FromAccountId);
            var to = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ToAccountId, ct)
                     ?? throw new AccountNotFoundException(request.ToAccountId);

            // Ném TRƯỚC SaveChanges ⇒ chưa có gì được ghi ⇒ key không bị claim ⇒ retry được (không kẹt).
            if (from.Balance < request.Amount)
                throw new InsufficientFundsException();

            from.Balance -= request.Amount; from.Version++;   // EF: UPDATE ... WHERE Version=@old
            to.Balance += request.Amount; to.Version++;

            var transfer = new Transfer
            {
                IdempotencyKey = request.IdempotencyKey,
                FromAccountId = from.Id,
                ToAccountId = to.Id,
                Amount = request.Amount,
                Status = TransferStatus.Completed
            };
            _db.Transfers.Add(transfer);

            // Double-entry: một cặp bút toán bất biến cho lệnh này.
            _db.LedgerEntries.Add(new LedgerEntry
            {
                TransferId = transfer.Id,
                AccountId = from.Id,
                Direction = LedgerDirection.Debit,
                Amount = request.Amount,
                BalanceAfter = from.Balance
            });
            _db.LedgerEntries.Add(new LedgerEntry
            {
                TransferId = transfer.Id,
                AccountId = to.Id,
                Direction = LedgerDirection.Credit,
                Amount = request.Amount,
                BalanceAfter = to.Balance
            });

            var result = new TransferMoneyResult(transfer.Id, from.Balance, "Completed");

            // Claim key Ở TRẠNG THÁI CUỐI (Completed) + response, INSERT thẳng: UNIQUE(Key) là trọng tài chống trùng.
            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = request.IdempotencyKey,
                Status = IdempotencyStatus.Completed,
                ResponseBody = JsonSerializer.Serialize(result)
            });

            try
            {
                // MỘT commit nguyên tử: số dư + 2 bút toán + transfer + key. Hoặc tất cả, hoặc không gì.
                await _db.SaveChangesAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Token số dư lệch: có giao dịch khác vừa đổi số dư tài khoản → đọc lại và thử lại.
                if (attempt == MaxRetries) throw new ConcurrentRequestException();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Request khác cùng key đã INSERT trước. Nếu nó đã Completed → trả cache; nếu chưa → thử lại.
                _db.ChangeTracker.Clear();
                var winner = await _db.IdempotencyRecords.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Key == request.IdempotencyKey, ct);
                if (winner is { Status: IdempotencyStatus.Completed, ResponseBody: not null })
                {
                    var cached = JsonSerializer.Deserialize<TransferMoneyResult>(winner.ResponseBody)!;
                    return cached with { ServedFromCache = true };
                }
                if (attempt == MaxRetries) throw new ConcurrentRequestException();
            }
        }

        // Hết số lần retry vì xung đột liên tục → hệ thống quá bận.
        throw new ConcurrentRequestException();
    }

    // Provider-agnostic: SQLite -> "UNIQUE constraint failed", Postgres -> "duplicate key value ... unique".
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
