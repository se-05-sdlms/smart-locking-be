using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>
/// Use case quản trị đặt lại ngăn. Mọi thao tác qua tài khoản phải kiểm tra phạm vi tủ;
/// ReceiveAsync là đầu vào adapter nhận phản hồi ESP, không phải API công khai cho resident/guest.
/// </summary>
public interface ICompartmentResetService
{
    // Chỉ đọc cấu hình, giao dịch gợi ý và yêu cầu Pending để người vận hành đối chiếu.
    Task<CompartmentResetContext> ContextAsync(Guid userId, string role, Guid lockerId, Guid compartmentId, CancellationToken ct = default);
    // Ghi yêu cầu và tạm ngừng cấp ngăn; chưa giải phóng hàng khi mới gửi lệnh.
    Task<CompartmentResetResponse> RequestAsync(Guid userId, string role, Guid lockerId, Guid compartmentId, CompartmentResetRequest request, CancellationToken ct = default);
    // Đọc kết quả đã lưu; không tự phát lại lệnh MQTT.
    Task<CompartmentResetResponse> GetAsync(Guid userId, string role, Guid commandId, CancellationToken ct = default);
    // HTTP/BLE unsigned không áp dụng nghiệp vụ; chỉ đọc kết quả đã lưu, sau kiểm tra quyền trên tủ.
    Task<CompartmentResetResponse> ConfirmAsync(Guid userId, string role, Guid commandId, string proof, CancellationToken ct = default);
    // Chỉ adapter MQTT: đối chiếu phản hồi và áp dụng một lần; ACK trùng không cập nhật lần hai.
    Task ReceiveAsync(string device, string proof, CancellationToken ct = default);
    // Chuyển hàng chỉ hoàn tất sau khi ESP đã xóa trạng thái/PIN của đúng kiện, không mở chốt.
    Task<DTOs.Parcels.OverdueTransferResponse> RequestTransferAsync(Guid userId, string role, Guid parcelId, CancellationToken ct = default);
    // Worker thử lại các yêu cầu chuyển hàng đã lưu, giữ cùng commandId qua restart/mất mạng.
    Task RetryPendingTransfersAsync(CancellationToken ct = default);
}
