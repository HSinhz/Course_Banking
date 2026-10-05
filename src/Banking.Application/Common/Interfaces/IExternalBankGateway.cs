namespace Banking.Application.Common.Interfaces;

/// <summary>Kết quả gọi ngân hàng ngoài (qua NAPAS/SWIFT mô phỏng).</summary>
public enum ExternalResult
{
    Succeeded = 0,  // bank ngoài đã ghi có cho người nhận
    Failed = 1,     // bank ngoài từ chối (dứt khoát) → phải hoàn tiền
    Unknown = 2     // timeout/không rõ → KHÔNG đoán, để reconcile xử lý
}

/// <summary>Cổng tới ngân hàng ngoài. Hệ thống thật gọi NAPAS/SWIFT; ở đây là giả lập.</summary>
public interface IExternalBankGateway
{
    /// <summary>Gửi lệnh ghi có sang bank ngoài. Idempotent theo transferId.</summary>
    Task<ExternalResult> SendAsync(Guid transferId, string? externalBankName, decimal amount, CancellationToken ct);

    /// <summary>Tra cứu lại trạng thái (dùng khi lần gửi trước trả Unknown/timeout).</summary>
    Task<ExternalResult> QueryStatusAsync(Guid transferId, decimal amount, CancellationToken ct);
}
