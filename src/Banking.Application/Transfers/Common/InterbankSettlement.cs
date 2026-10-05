using Banking.Application.Common.Interfaces;
using Banking.Domain.Accounts;
using Banking.Domain.Ledger;
using Banking.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Transfers.Common;

/// <summary>
/// Áp kết quả từ bank ngoài lên một lệnh liên NH đang <see cref="TransferStatus.Pending"/> (treo ở suspense).
/// Chỉ STAGE thay đổi (không SaveChanges) — caller gói trong transaction của mình.
/// Dùng chung cho worker outbox và reconciliation. An toàn idempotent: chỉ tác động khi còn Pending.
/// </summary>
public static class InterbankSettlement
{
    /// <returns>true nếu lệnh đã được giải quyết (Posted/Reversed); false nếu Unknown (giữ Pending).</returns>
    public static async Task<bool> ApplyAsync(
        IBankingDbContext db, Transfer transfer, ExternalResult result, CancellationToken ct)
    {
        if (transfer.Status != TransferStatus.Pending)
            return true; // đã được xử lý ở nơi khác — không làm gì thêm (gác 2 lần)

        if (result == ExternalResult.Unknown)
            return false; // KHÔNG đoán — để reconcile lần sau

        var suspense = await db.Accounts.FirstAsync(a => a.Id == WellKnownAccounts.SuspenseId, ct);
        var amount = transfer.Amount;

        // Rời khỏi suspense trong cả hai trường hợp (tiền không còn "đang bay").
        suspense.Balance -= amount; suspense.Version++;

        if (result == ExternalResult.Succeeded)
        {
            // Tiền RỜI hệ thống sang bank ngoài → đối ứng vào EXTERNAL_OUT để bút toán vẫn cân.
            var externalOut = await db.Accounts.FirstAsync(a => a.Id == WellKnownAccounts.ExternalOutId, ct);
            externalOut.Balance += amount; externalOut.Version++;

            AddPair(db, transfer.Id, suspense.Id, suspense.Balance, externalOut.Id, externalOut.Balance, amount);
            transfer.Status = TransferStatus.Completed; // Posted
        }
        else // Failed → REVERSAL: hoàn tiền về nguồn
        {
            var source = await db.Accounts.FirstAsync(a => a.Id == transfer.FromAccountId, ct);
            source.Balance += amount; source.Version++;

            AddPair(db, transfer.Id, suspense.Id, suspense.Balance, source.Id, source.Balance, amount);
            transfer.Status = TransferStatus.Reversed;
        }

        return true;
    }

    // Một cặp bút toán ghi kép: Debit tài khoản ra, Credit tài khoản vào.
    private static void AddPair(
        IBankingDbContext db, Guid transferId,
        Guid debitAccountId, decimal debitBalanceAfter,
        Guid creditAccountId, decimal creditBalanceAfter, decimal amount)
    {
        db.LedgerEntries.Add(new LedgerEntry
        {
            TransferId = transferId, AccountId = debitAccountId,
            Direction = LedgerDirection.Debit, Amount = amount, BalanceAfter = debitBalanceAfter
        });
        db.LedgerEntries.Add(new LedgerEntry
        {
            TransferId = transferId, AccountId = creditAccountId,
            Direction = LedgerDirection.Credit, Amount = amount, BalanceAfter = creditBalanceAfter
        });
    }
}
