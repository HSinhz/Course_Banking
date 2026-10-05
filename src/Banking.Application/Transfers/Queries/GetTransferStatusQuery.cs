using Banking.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Transfers.Queries;

public record GetTransferStatusQuery(Guid TransferId) : IRequest<TransferStatusDto?>;

public record TransferStatusDto(Guid Id, string Status, string Kind, decimal Amount, string? ExternalBankName, DateTime CreatedAt);

public class GetTransferStatusQueryHandler(IBankingDbContext db)
    : IRequestHandler<GetTransferStatusQuery, TransferStatusDto?>
{
    public async Task<TransferStatusDto?> Handle(GetTransferStatusQuery request, CancellationToken ct)
    {
        return await db.Transfers.AsNoTracking()
            .Where(t => t.Id == request.TransferId)
            .Select(t => new TransferStatusDto(t.Id, t.Status.ToString(), t.Kind.ToString(), t.Amount, t.ExternalBankName, t.CreatedAt))
            .FirstOrDefaultAsync(ct);
    }
}
