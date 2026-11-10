namespace smart_locking_be.Infrastructure.Options;

public sealed class ResidentSubscriptionOptions
{
    public const string SectionName = "ResidentSubscription";

    /// <summary>
    /// Bật thì Cư dân phải có gói đang hiệu lực và chưa vượt MaxActiveParcels mới được nhận kiện mới.
    /// Tắt cho môi trường dev/test chưa có dữ liệu gói.
    /// </summary>
    public bool EnforceParcelLimits { get; set; } = true;
}
