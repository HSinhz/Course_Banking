using Banking.Application.Common.Interfaces;
using Banking.Application.Transfers.Common;
using Banking.Domain.Transfers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Transfers.Commands.Reconcile;

/// <summary>
/// Lưới an toàn cuối: quét các lệnh liên NH còn Pending (bank ngoài từng trả Unknown/timeout),
/// tra cứu lại trạng thái và settle/reverse. Trả số lệnh đã giải quyết.
/// </summary>
public record ReconcileCommand : IRequest<int>;

public class ReconcileCommandHandler(IBankingDbContext db, IExternalBankGateway gateway)
    : IRequestHandler<ReconcileCommand, int>
{
    public async Task<int> Handle(ReconcileCommand request, CancellationToken ct)
    {
        var pendingIds = await db.Transfers
            .Where(t => t.Kind == TransferKind.Interbank && t.Status == TransferStatus.Pending)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var resolved = 0;
        foreach (var id in pendingIds)
        {
            db.ChangeTracker.Clear();
            var transfer = await db.Transfers.FirstAsync(t => t.Id == id, ct);
            var status = await gateway.QueryStatusAsync(transfer.Id, transfer.Amount, ct);

            if (await InterbankSettlement.ApplyAsync(db, transfer, status, ct))
            {
                // Đóng luôn outbox message tương ứng (nếu còn Pending).
                var msg = await db.OutboxMessages.FirstOrDefaultAsync(m => m.TransferId == id, ct);
                if (msg is not null && msg.Status == Domain.Outbox.OutboxStatus.Pending)
                {
                    msg.Status = Domain.Outbox.OutboxStatus.Processed;
                    msg.ProcessedAt = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(ct);
                resolved++;
            }
        }
        return resolved;
    }
}
