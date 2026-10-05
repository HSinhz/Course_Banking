using Banking.Domain.Common;

namespace Banking.Domain.Transfers;

public enum TransferStatus
{
    Pending = 0,    // đã ghi nợ nguồn → treo ở suspense, chờ bank ngoài (chỉ dùng cho Interbank)
    Completed = 1,  // hoàn tất (nội bộ: xong ngay; liên NH: bank ngoài đã nhận = Posted)
    Failed = 2,
    Reversed = 3    // bank ngoài từ chối → đã hoàn tiền về nguồn
}

public enum TransferKind
{
    Internal = 0,   // chuyển nội bộ (Giai đoạn 1) — atomic, đi thẳng Completed
    Interbank = 1   // chuyển liên ngân hàng (Giai đoạn 2) — qua suspense + saga
}

public class Transfer : BaseEntity
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Completed;
    public TransferKind Kind { get; set; } = TransferKind.Internal;

    /// <summary>Tên ngân hàng thụ hưởng bên ngoài (chỉ dùng cho Interbank).</summary>
    public string? ExternalBankName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
