namespace smart_locking_be.Application.DTOs.DeliveryRequests;

/// <summary>
/// Kết quả đọc số điện thoại người nhận từ ảnh kiện hàng. Status là Succeeded hoặc Failed;
/// Failed không phải lỗi, Shipper chỉ cần nhập số thủ công.
/// </summary>
public sealed record WaybillOcrResponse(
    string Status,
    IReadOnlyList<string> Phones,
    bool IsMasked,
    string ParcelImageUrl);
