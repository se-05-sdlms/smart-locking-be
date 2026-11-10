namespace smart_locking_be.Application.DTOs.DeliveryRequests;

/// <summary>
/// ParcelImageUrl có thể bỏ trống khi ảnh đã được tải lên qua :waybill-ocr; khi đó dùng ảnh đã lưu.
/// </summary>
public sealed record SubmitDeliveryRequest(string? ParcelImageUrl, string RecipientPhone);
