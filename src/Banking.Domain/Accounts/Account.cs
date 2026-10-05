using Banking.Domain.Common;

namespace Banking.Domain.Accounts;

public class Account : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Balance { get; set; }

    /// <summary>
    /// Concurrency token (app-managed) chống đua số dư (lost update). Tự tăng mỗi lần đổi <see cref="Balance"/>.
    /// Khai báo IsConcurrencyToken() ở BankingDbContext → EF sinh WHERE Version=@old khi UPDATE;
    /// lệch → DbUpdateConcurrencyException. Dùng int (không rowversion/xmin) để chạy cả Postgres lẫn SQLite.
    /// </summary>
    public int Version { get; set; }
}
