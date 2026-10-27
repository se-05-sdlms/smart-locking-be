using smart_locking_be.Application.DTOs.Subscriptions;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IParcelQuotaService
{
    /// <summary>
    /// Kiểm tra Cư dân có được nhận thêm kiện mới hay không theo gói tài khoản đang hiệu lực.
    /// </summary>
    Task<ParcelQuotaResult> CheckAsync(Guid residentProfileId, CancellationToken cancellationToken = default);
}
