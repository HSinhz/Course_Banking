using Banking.Domain.Accounts;
using Banking.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Tests;

/// <summary>
/// DB SQLite in-memory dùng "Cache=Shared" nên nhiều DbContext/kết nối cùng thấy 1 database
/// (bắt buộc cho test concurrency). "Default Timeout" cho phép writer chờ nhau thay vì lỗi ngay,
/// nhờ đó ràng buộc UNIQUE hoạt động sạch dưới đua tranh — mô phỏng đúng hành vi Postgres của HCM.
/// Mỗi fixture là một database riêng (tên ngẫu nhiên) => các test cô lập nhau.
/// </summary>
public sealed class BankDbFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _cs;

    public Guid AliceId { get; } = Guid.NewGuid();
    public Guid BobId { get; } = Guid.NewGuid();

    public BankDbFixture(decimal aliceBalance = 1000m)
    {
        _cs = $"Data Source=bank_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=5";
        _keepAlive = new SqliteConnection(_cs);
        _keepAlive.Open(); // giữ DB sống suốt vòng đời fixture

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
        ctx.Accounts.Add(new Account { Id = AliceId, Name = "Alice", Balance = aliceBalance });
        ctx.Accounts.Add(new Account { Id = BobId, Name = "Bob", Balance = 0m });
        // Tài khoản hệ thống cho luồng liên ngân hàng (Giai đoạn 2).
        ctx.Accounts.Add(new Account { Id = WellKnownAccounts.SuspenseId, Name = "SUSPENSE", Balance = 0m });
        ctx.Accounts.Add(new Account { Id = WellKnownAccounts.ExternalOutId, Name = "EXTERNAL_OUT", Balance = 0m });
        ctx.SaveChanges();
    }

    /// <summary>Mỗi lần gọi = một DbContext mới (mô phỏng 1 request/scope riêng).</summary>
    public BankingDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BankingDbContext>().UseSqlite(_cs).Options);

    public void Dispose() => _keepAlive.Dispose();
}
