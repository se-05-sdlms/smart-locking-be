namespace smart_locking_be.Application.DTOs.DeliveryRequests;

/// <summary>
/// Kết quả thô từ dịch vụ OCR: các số điện thoại người nhận đã chuẩn hóa về dạng 0xxxxxxxxx.
/// </summary>
public sealed record WaybillOcrResult(bool Succeeded, IReadOnlyList<string> Phones, bool IsMasked)
{
    public static WaybillOcrResult Failed { get; } = new(false, [], false);
}
