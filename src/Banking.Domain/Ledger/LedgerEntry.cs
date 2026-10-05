using Banking.Domain.Common;

namespace Banking.Domain.Ledger;

/// <summary>Chiều bút toán trong sổ ghi kép.</summary>
public enum LedgerDirection
{
    Debit = 0,   // ghi Nợ  → giảm số dư tài khoản giao dịch nội bộ
    Credit = 1   // ghi Có → tăng số dư
}

/// <summary>
/// Bút toán sổ cái — <b>bất biến (append-only)</b>: chỉ INSERT, không sửa/xoá.
/// Mỗi lệnh chuyển tiền sinh MỘT cặp: Debit(nguồn) + Credit(đích).
/// Bất biến kế toán: với cùng <see cref="TransferId"/>, tổng Amount các Debit = tổng Amount các Credit.
/// </summary>
public class LedgerEntry : BaseEntity
{
    /// <summary>Gom các bút toán thuộc cùng một lệnh chuyển tiền.</summary>
    public Guid TransferId { get; set; }

    public Guid AccountId { get; set; }

    public LedgerDirection Direction { get; set; }

    /// <summary>Số tiền của bút toán — luôn &gt; 0. Chiều do <see cref="Direction"/> quyết định.</summary>
    public decimal Amount { get; set; }

    /// <summary>Số dư tài khoản NGAY SAU bút toán này (dữ liệu kiểm toán, không dùng làm nguồn số dư).</summary>
    public decimal BalanceAfter { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Hiệu ứng có dấu lên số dư: Credit = +Amount, Debit = −Amount.</summary>
    public decimal SignedEffect => Direction == LedgerDirection.Credit ? Amount : -Amount;
}
