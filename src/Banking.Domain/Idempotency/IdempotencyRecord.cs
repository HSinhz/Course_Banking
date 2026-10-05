namespace Banking.Domain.Idempotency;

public enum IdempotencyStatus
{
    InProgress = 0,
    Completed = 1,
    Failed = 2
}

/// <summary>
/// Bản ghi chống trùng. <see cref="Key"/> là PRIMARY KEY nên DB tự áp UNIQUE —
/// đây chính là "trọng tài" ép idempotent: INSERT lần 2 cùng key sẽ vi phạm ràng buộc.
/// </summary>
public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; } = IdempotencyStatus.InProgress;

    /// <summary>Response đã lưu (JSON) để trả lại nguyên vẹn cho request lặp lại.</summary>
    public string? ResponseBody { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
