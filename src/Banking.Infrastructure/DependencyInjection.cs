using Banking.Application.Common.Interfaces;
using Banking.Infrastructure.Auth;
using Banking.Infrastructure.External;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BankingDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IBankingDbContext>(sp => sp.GetRequiredService<BankingDbContext>());

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IExternalBankGateway, SimulatedExternalBankGateway>();

        return services;
    }
}
