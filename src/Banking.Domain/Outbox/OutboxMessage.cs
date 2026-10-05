using Banking.Domain.Common;

namespace Banking.Domain.Outbox;

public enum OutboxStatus
{
    Pending = 0,
    Processed = 1,
    Failed = 2
}

/// <summary>
/// Transactional Outbox: "việc cần làm tiếp" được ghi CÙNG transaction với chuyển động tiền.
/// Worker nền đọc và hoàn tất ⇒ dù process crash, lệnh vẫn không mất (at-least-once + xử lý idempotent).
/// </summary>
public class OutboxMessage : BaseEntity
{
    public string Type { get; set; } = string.Empty;   // vd "SettleInterbankTransfer"
    public Guid TransferId { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}
