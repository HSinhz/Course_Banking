using Banking.Application.Common.Interfaces;
using Banking.Domain.Accounts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Accounts.Queries;

public record GetAccountsQuery : IRequest<IReadOnlyList<AccountDto>>;

public record AccountDto(Guid Id, string Name, decimal Balance);

public class GetAccountsQueryHandler(IBankingDbContext db)
    : IRequestHandler<GetAccountsQuery, IReadOnlyList<AccountDto>>
{
    public async Task<IReadOnlyList<AccountDto>> Handle(GetAccountsQuery request, CancellationToken ct)
    {
        // Ẩn tài khoản hệ thống (SUSPENSE, EXTERNAL_OUT) khỏi UI khách hàng.
        var systemIds = new[] { WellKnownAccounts.SuspenseId, WellKnownAccounts.ExternalOutId };
        return await db.Accounts
            .AsNoTracking()
            .Where(a => !systemIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Select(a => new AccountDto(a.Id, a.Name, a.Balance))
            .ToListAsync(ct);
    }
}
