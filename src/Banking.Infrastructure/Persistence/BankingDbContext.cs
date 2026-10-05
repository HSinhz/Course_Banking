using Banking.Application.Common.Interfaces;
using Banking.Domain.Accounts;
using Banking.Domain.Idempotency;
using Banking.Domain.Ledger;
using Banking.Domain.Outbox;
using Banking.Domain.Transfers;
using Banking.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Persistence;

public class BankingDbContext(DbContextOptions<BankingDbContext> options)
    : DbContext(options), IBankingDbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Balance).HasColumnType("decimal(18,2)");
            // Concurrency token chống đua số dư: EF thêm WHERE Version=@old vào UPDATE.
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        b.Entity<Transfer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.ExternalBankName).HasMaxLength(200);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => x.Status);
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.BalanceAfter).HasColumnType("decimal(18,2)");
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => x.TransferId);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            // Key là PRIMARY KEY -> UNIQUE tự động = trọng tài chống trùng.
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(100);
            e.HasIndex(x => x.Username).IsUnique();
        });

        base.OnModelCreating(b);
    }
}
