namespace Banking.Domain.Accounts;

/// <summary>
/// Các tài khoản hệ thống (nội bộ kế toán, không phải tài khoản khách):
/// - SUSPENSE: giữ tiền "đang bay" của lệnh liên NH (chờ bank ngoài) ⇒ luôn kiểm đếm được.
/// - EXTERNAL_OUT: đối ứng double-entry khi tiền RỜI hệ thống sang bank ngoài (nostro/NAPAS).
/// Nhờ EXTERNAL_OUT, mọi bút toán luôn cân (tổng hiệu ứng có dấu = 0) kể cả khi tiền ra ngoài.
/// </summary>
public static class WellKnownAccounts
{
    public static readonly Guid SuspenseId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    public static readonly Guid ExternalOutId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public static bool IsSystem(Guid id) => id == SuspenseId || id == ExternalOutId;
}
