using Banking.Domain.Accounts;
using Banking.Domain.Idempotency;
using Banking.Domain.Ledger;
using Banking.Domain.Outbox;
using Banking.Domain.Transfers;
using Banking.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Banking.Application.Common.Interfaces;

/// <summary>
/// Cổng truy cập DB cho tầng Application (giống IApplicationDbContext trong Clean Architecture của HCM).
/// Infrastructure hiện thực bằng EF Core; Application không biết provider cụ thể (Postgres/SQLite...).
/// </summary>
public interface IBankingDbContext
{
    DbSet<Account> Accounts { get; }
    DbSet<Transfer> Transfers { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<User> Users { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
