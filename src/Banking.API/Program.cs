using System.Text;
using Banking.API;
using Banking.Application;
using Banking.Application.Common.Interfaces;
using Banking.Domain.Accounts;
using Banking.Domain.Users;
using Banking.Infrastructure;
using Banking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Cổng cố định để Vite proxy trỏ tới.
builder.WebHost.UseUrls("http://localhost:5199");

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=127.0.0.1;Port=55432;Database=banking_basic;Username=postgres;Password=admin";

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddControllers();
builder.Services.AddHostedService<OutboxProcessorHostedService>();

// JWT auth (HS256)
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!))
        };
    });
builder.Services.AddAuthorization();

// CORS cho Vite dev server
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins("http://localhost:5180", "http://localhost:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

// Tạo DB + seed user demo và 2 tài khoản.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    db.Database.EnsureCreated();

    if (!db.Users.Any())
    {
        db.Users.Add(new User
        {
            Username = "demo",
            DisplayName = "Demo User",
            PasswordHash = hasher.Hash("demo123")
        });
    }
    if (!db.Accounts.Any())
    {
        db.Accounts.Add(new Account { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Alice", Balance = 1000m });
        db.Accounts.Add(new Account { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Bob", Balance = 0m });
    }
    // Tài khoản khách bổ sung (idempotent: thêm nếu chưa có, kể cả khi DB đã tạo trước đó).
    var charlieId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    if (!db.Accounts.Any(a => a.Id == charlieId))
        db.Accounts.Add(new Account { Id = charlieId, Name = "Charlie", Balance = 500m });
    // Tài khoản hệ thống cho luồng liên ngân hàng (Giai đoạn 2).
    if (!db.Accounts.Any(a => a.Id == WellKnownAccounts.SuspenseId))
        db.Accounts.Add(new Account { Id = WellKnownAccounts.SuspenseId, Name = "SUSPENSE", Balance = 0m });
    if (!db.Accounts.Any(a => a.Id == WellKnownAccounts.ExternalOutId))
        db.Accounts.Add(new Account { Id = WellKnownAccounts.ExternalOutId, Name = "EXTERNAL_OUT", Balance = 0m });
    db.SaveChanges();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => "Banking Basic API — POST /api/v1/auth/login (demo/demo123).");

app.Run();
