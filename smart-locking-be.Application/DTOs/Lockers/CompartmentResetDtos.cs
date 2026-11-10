namespace smart_locking_be.Application.DTOs.Lockers;

// ExpectedSubjectId phải là giao dịch của ngăn; ConfirmedEmpty là xác nhận đã lấy hết hàng bằng tay.
// UseBluetooth chỉ chọn đường chuyển lệnh, không bỏ qua phân quyền hoặc bước xác nhận ESP.
public sealed record CompartmentResetRequest(Guid ExpectedSubjectId, bool ConfirmedEmpty, string Reason, bool UseBluetooth = false);
// Chuỗi phản hồi đọc từ ESP; client không được tự suy ra OK từ trạng thái cửa đóng.
public sealed record CompartmentResetConfirmation(string Proof);
// SuggestedSubjectId chỉ hỗ trợ đối chiếu; server vẫn kiểm tra lại khi gửi yêu cầu.
public sealed record CompartmentResetContext(string DeviceIdentifier, int Channel, Guid? SuggestedSubjectId, Guid? PendingCommandId);
// Pending chưa hoàn tất; Applied chỉ xuất hiện sau khi phản hồi OK đã được áp dụng/lưu tại BE.
public sealed record CompartmentResetResponse(Guid CommandId, string Status, string DeviceIdentifier, int Channel,
    string? Command, DateTimeOffset ExpiresAt, string Message);
