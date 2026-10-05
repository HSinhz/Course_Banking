using Banking.Application.Common.Interfaces;

namespace Banking.Infrastructure.External;

/// <summary>
/// Ngân hàng ngoài GIẢ LẬP (thay cho NAPAS/SWIFT), kết quả TẤT ĐỊNH theo phần lẻ số tiền để demo/test:
///   - lẻ .13  → Failed  (bank ngoài từ chối)
///   - lẻ .99  → Unknown (timeout/không rõ) — lần gửi đầu; khi reconcile tra lại → coi như Failed để giải quyết
///   - còn lại → Succeeded
/// Hệ thống thật gọi mạng; ở đây không có side-effect ngoài.
/// </summary>
public sealed class SimulatedExternalBankGateway : IExternalBankGateway
{
    private static int Cents(decimal amount) => (int)Math.Round((amount - Math.Truncate(amount)) * 100m);

    public Task<ExternalResult> SendAsync(Guid transferId, string? externalBankName, decimal amount, CancellationToken ct)
        => Task.FromResult(Cents(amount) switch
        {
            13 => ExternalResult.Failed,
            99 => ExternalResult.Unknown,
            _ => ExternalResult.Succeeded
        });

    // Khi tra cứu lại (reconcile): .99 chuyển thành Failed để lệnh treo được giải quyết dứt điểm.
    public Task<ExternalResult> QueryStatusAsync(Guid transferId, decimal amount, CancellationToken ct)
        => Task.FromResult(Cents(amount) switch
        {
            13 => ExternalResult.Failed,
            _ => ExternalResult.Succeeded
        });
}
