using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>
/// Cổng chuyển đổi giữa dữ liệu nghiệp vụ và định dạng ESP. Đọc hợp lệ về cú pháp
/// không thay thế kiểm tra quyền sở hữu, session, phí hay trạng thái giao dịch tại service.
/// </summary>
public interface IBluetoothMessageCodec
{
    string CreateCommand(string device, Guid subject, int channel, string kind, string phone, string pin, DateTimeOffset expires, Guid commandId);
    // Chỉ đọc nhật ký mở-rồi-đóng cửa; service phải đối chiếu và lưu trước khi ACK cho ESP xóa.
    OfflinePinEvidence ParseOfflineEvent(string proof, string device);
    (Guid DeliveryId, string Pin) ParsePinProvisioned(string proof, string device);
    // ACK cho đúng eventId đã tiếp nhận, không phải lệnh mở chốt hoặc reset ngăn.
    string CreateOfflineAcknowledgment(string device, Guid eventId);
}
