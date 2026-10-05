namespace Banking.Domain.Common;

/// <summary>Base cho các entity có khóa Guid (giống TenantEntity/BaseEntity của HCM, rút gọn).</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
