namespace smart_locking_be.Domain.Enums;

/// <summary>
/// Nguồn phát sinh hoặc đối tượng báo cáo sự cố (incident).
/// </summary>
public enum IncidentSource
{
    /// <summary>
    /// Sự cố do Cư dân báo cáo.
    /// </summary>
    Resident,

    /// <summary>
    /// Sự cố do Shipper báo cáo.
    /// </summary>
    Shipper,

    /// <summary>
    /// Sự cố do hệ thống tự động phát hiện và ghi nhận.
    /// </summary>
    System
}

/// <summary>
/// Trạng thái xử lý sự cố.
/// </summary>
public enum IncidentStatus
{
    /// <summary>
    /// Sự cố mới được tạo, chưa xử lý.
    /// </summary>
    Open,

    /// <summary>
    /// Sự cố đang được Nhân viên vận hành điều tra và xử lý.
    /// </summary>
    Investigating,

    /// <summary>
    /// Sự cố đã được khắc phục và giải quyết thành công.
    /// </summary>
    Resolved,

    /// <summary>
    /// Sự cố vượt quá thẩm quyền đã được chuyển cấp lên Quản trị viên xử lý.
    /// </summary>
    Escalated
}

/// <summary>
/// Phân loại sự kiện vận hành hoặc telemetry từ thiết bị locker.
/// </summary>
public enum LockerEventType
{
    /// <summary>
    /// Trạng thái kết nối mạng của locker thay đổi (Online/Offline).
    /// </summary>
    ConnectionChanged,

    /// <summary>
    /// Cảm biến cửa ngăn locker thay đổi trạng thái (Open/Closed).
    /// </summary>
    DoorChanged,

    /// <summary>
    /// Trạng thái vận hành của locker hoặc ngăn bị thay đổi.
    /// </summary>
    OperationalStatusChanged,

    /// <summary>
    /// Cảnh báo thiết bị phần cứng từ locker.
    /// </summary>
    DeviceAlert
}

/// <summary>
/// Mức độ nghiêm trọng của sự kiện locker.
/// </summary>
public enum LockerEventSeverity
{
    /// <summary>
    /// Mức thông tin thông thường.
    /// </summary>
    Info,

    /// <summary>
    /// Mức cảnh báo cần lưu ý.
    /// </summary>
    Warning,

    /// <summary>
    /// Mức nghiêm trọng cần xử lý ngay.
    /// </summary>
    Critical
}

/// <summary>
/// Kết quả xử lý lệnh mở khóa khẩn cấp.
/// </summary>
public enum EmergencyUnlockResult
{
    /// <summary>
    /// Lệnh mở khóa khẩn cấp đang chờ thực thi hoặc chờ phản hồi từ thiết bị.
    /// </summary>
    Pending,

    /// <summary>
    /// Mở khóa khẩn cấp thành công.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Mở khóa khẩn cấp thất bại.
    /// </summary>
    Failed
}

/// <summary>
/// Mức độ ưu tiên của yêu cầu bảo trì.
/// </summary>
public enum MaintenancePriority
{
    /// <summary>
    /// Mức ưu tiên thấp.
    /// </summary>
    Low,

    /// <summary>
    /// Mức ưu tiên bình thường.
    /// </summary>
    Normal,

    /// <summary>
    /// Mức ưu tiên cao.
    /// </summary>
    High,

    /// <summary>
    /// Mức khẩn cấp.
    /// </summary>
    Critical
}

/// <summary>
/// Trạng thái vòng đời của yêu cầu bảo trì.
/// </summary>
public enum MaintenanceStatus
{
    /// <summary>
    /// Yêu cầu bảo trì mới được tạo.
    /// </summary>
    Open,

    /// <summary>
    /// Công tác bảo trì đang được tiến hành.
    /// </summary>
    InProgress,

    /// <summary>
    /// Yêu cầu bảo trì đã hoàn thành và đóng.
    /// </summary>
    Closed,

    /// <summary>
    /// Yêu cầu bảo trì bị hủy.
    /// </summary>
    Cancelled
}
