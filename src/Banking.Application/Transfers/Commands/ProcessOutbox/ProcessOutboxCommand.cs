using Banking.Application.Common.Interfaces;
using Banking.Application.Transfers.Common;
using Banking.Domain.Outbox;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Transfers.Commands.ProcessOutbox;

/// <summary>Xử lý các OutboxMessage đang Pending (gọi bank ngoài rồi settle/reverse). Trả số message đã xử lý.</summary>
public record ProcessOutboxCommand(int MaxMessages = 50) : IRequest<int>;

public class ProcessOutboxCommandHandler(IBankingDbContext db, IExternalBankGateway gateway)
    : IRequestHandler<ProcessOutboxCommand, int>
{
    private const int MaxRetries = 3;

    public async Task<int> Handle(ProcessOutboxCommand request, CancellationToken ct)
    {
        var pending = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(request.MaxMessages)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var handled = 0;
        foreach (var id in pending)
            if (await ProcessOneAsync(id, ct)) handled++;
        return handled;
    }

    private async Task<bool> ProcessOneAsync(Guid outboxId, CancellationToken ct)
    {
        // Gọi bank ngoài NGOÀI transaction (side-effect mạng), rồi ghi kết quả trong 1 transaction có retry.
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            db.ChangeTracker.Clear();

            var msg = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == outboxId, ct);
            if (msg is null || msg.Status != OutboxStatus.Pending) return false;

            var transfer = await db.Transfers.FirstAsync(t => t.Id == msg.TransferId, ct);

            var result = await gateway.SendAsync(transfer.Id, transfer.ExternalBankName, transfer.Amount, ct);
            var resolved = await InterbankSettlement.ApplyAsync(db, transfer, result, ct);

            if (resolved)
            {
                msg.Status = OutboxStatus.Processed;
                msg.ProcessedAt = DateTime.UtcNow;
            }
            else
            {
                msg.Attempts++;
                msg.LastError = "external bank returned Unknown/timeout";
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return resolved;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt == MaxRetries) return false; // để reconcile/worker lần sau xử lý
            }
        }
        return false;
    }
}
