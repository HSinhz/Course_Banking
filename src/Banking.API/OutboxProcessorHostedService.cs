using Banking.Application.Transfers.Commands.ProcessOutbox;
using MediatR;

namespace Banking.API;

/// <summary>
/// Worker nền: định kỳ xử lý OutboxMessage đang Pending (gọi bank ngoài + settle/reverse).
/// Nhờ outbox, dù request đã trả về "Pending" và process có khởi động lại, lệnh vẫn được hoàn tất.
/// </summary>
public class OutboxProcessorHostedService(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessorHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
                var handled = await mediator.Send(new ProcessOutboxCommand(), stoppingToken);
                if (handled > 0) logger.LogInformation("Outbox: đã xử lý {Count} lệnh liên ngân hàng.", handled);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox worker gặp lỗi, sẽ thử lại.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
