namespace smart_locking_be.Application.DTOs.DeliveryRequests;

/// <summary>
/// Họ tên đầy đủ của Cư dân nhận hàng để Shipper đối chiếu với vận đơn. Không trả thêm thông tin nào khác.
/// </summary>
public sealed record ResolveRecipientResponse(string FullName);
