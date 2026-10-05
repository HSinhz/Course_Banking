using System.Text.Json;
using Banking.Application.Common.Exceptions;
using Banking.Application.Common.Interfaces;
using Banking.Domain.Accounts;
using Banking.Domain.Idempotency;
using Banking.Domain.Ledger;
using Banking.Domain.Outbox;
using Banking.Domain.Transfers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Transfers.Commands.Interbank;

public record InterbankTransferCommand(
    string IdempotencyKey,
    Guid FromAccountId,
    string ExternalBankName,
    decimal Amount) : IRequest<InterbankTransferResult>;

public record InterbankTransferResult(Guid TransferId, string Status, bool ServedFromCache = false);

/// <summary>
/// Bước 1 của saga liên NH (atomic, idempotent): ghi nợ nguồn → treo vào SUSPENSE, tạo Transfer(Pending)
/// và OutboxMessage "SettleInterbankTransfer" trong CÙNG transaction. Tiền đã rời nguồn nhưng chưa ra ngoài.
/// Bước 2 (gọi bank ngoài + settle/reverse) do worker outbox thực hiện.
/// </summary>
public class InterbankTransferCommandHandler(IBankingDbContext db)
    : IRequestHandler<InterbankTransferCommand, InterbankTransferResult>
{
    private const int MaxRetries = 3;
    private readonly IBankingDbContext _db = db;

    public async Task<InterbankTransferResult> Handle(InterbankTransferCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new MissingIdempotencyKeyException();

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            _db.ChangeTracker.Clear();

            var existing = await _db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == request.IdempotencyKey, ct);
            if (existing is not null)
            {
                if (existing.Status == IdempotencyStatus.Completed && existing.ResponseBody is not null)
                    return JsonSerializer.Deserialize<InterbankTransferResult>(existing.ResponseBody)! with { ServedFromCache = true };

                _db.IdempotencyRecords.Remove(
                    await _db.IdempotencyRecords.FirstAsync(r => r.Key == request.IdempotencyKey, ct));
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
            }

            var from = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId, ct)
                       ?? throw new AccountNotFoundException(request.FromAccountId);
            var suspense = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == WellKnownAccounts.SuspenseId, ct)
                           ?? throw new AccountNotFoundException(WellKnownAccounts.SuspenseId);

            if (from.Balance < request.Amount)
                throw new InsufficientFundsException();

            from.Balance -= request.Amount; from.Version++;
            suspense.Balance += request.Amount; suspense.Version++;

            var transfer = new Transfer
            {
                IdempotencyKey = request.IdempotencyKey,
                FromAccountId = from.Id,
                ToAccountId = WellKnownAccounts.SuspenseId,
                Amount = request.Amount,
                Kind = TransferKind.Interbank,
                Status = TransferStatus.Pending,
                ExternalBankName = request.ExternalBankName
            };
            _db.Transfers.Add(transfer);

            _db.LedgerEntries.Add(new LedgerEntry
            {
                TransferId = transfer.Id, AccountId = from.Id,
                Direction = LedgerDirection.Debit, Amount = request.Amount, BalanceAfter = from.Balance
            });
            _db.LedgerEntries.Add(new LedgerEntry
            {
                TransferId = transfer.Id, AccountId = suspense.Id,
                Direction = LedgerDirection.Credit, Amount = request.Amount, BalanceAfter = suspense.Balance
            });

            _db.OutboxMessages.Add(new OutboxMessage
            {
                Type = "SettleInterbankTransfer",
                TransferId = transfer.Id,
                Status = OutboxStatus.Pending
            });

            var result = new InterbankTransferResult(transfer.Id, "Pending");
            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = request.IdempotencyKey,
                Status = IdempotencyStatus.Completed,
                ResponseBody = JsonSerializer.Serialize(result)
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt == MaxRetries) throw new ConcurrentRequestException();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _db.ChangeTracker.Clear();
                var winner = await _db.IdempotencyRecords.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Key == request.IdempotencyKey, ct);
                if (winner is { Status: IdempotencyStatus.Completed, ResponseBody: not null })
                    return JsonSerializer.Deserialize<InterbankTransferResult>(winner.ResponseBody)! with { ServedFromCache = true };
                if (attempt == MaxRetries) throw new ConcurrentRequestException();
            }
        }

        throw new ConcurrentRequestException();
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
