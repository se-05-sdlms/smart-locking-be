using smart_locking_be.Application.DTOs.DeliveryRequests;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IWaybillOcrService
{
    /// <summary>
    /// Đọc số điện thoại người nhận từ ảnh kiện hàng có dán vận đơn. Không bao giờ ném lỗi: mọi sự cố
    /// (thiếu cấu hình, timeout, ảnh mờ, nhà cung cấp lỗi) đều trả <see cref="WaybillOcrResult.Failed"/>.
    /// </summary>
    Task<WaybillOcrResult> ExtractRecipientPhonesAsync(
        byte[] image,
        string contentType,
        CancellationToken cancellationToken = default);
}
