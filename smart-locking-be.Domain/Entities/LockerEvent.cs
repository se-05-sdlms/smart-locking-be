using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu sự kiện vận hành hoặc telemetry quan trọng từ thiết bị locker hoặc ngăn locker.
/// </summary>
public sealed class LockerEvent
{
    /// <summary>
    /// Mã định danh duy nhất của sự kiện locker.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh tủ locker phát sinh sự kiện.
    /// </summary>
    public Guid LockerId { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker liên quan nếu sự kiện ở cấp ngăn.
    /// </summary>
    public Guid? LockerCompartmentId { get; set; }

    /// <summary>
    /// Mã định danh người dùng gây ra sự kiện nếu xuất phát từ thao tác người dùng; null với phần cứng/hệ thống tự động.
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Loại sự kiện locker (kết nối, cửa, trạng thái vận hành hoặc cảnh báo phần cứng).
    /// </summary>
    public LockerEventType EventType { get; set; }

    /// <summary>
    /// Giá trị trước khi thay đổi nếu sự kiện mô tả sự thay đổi trạng thái.
    /// </summary>
    public string? PreviousValue { get; set; }

    /// <summary>
    /// Giá trị mới sau khi thay đổi nếu sự kiện mô tả sự thay đổi trạng thái.
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// Mức độ nghiêm trọng của sự kiện (Info, Warning, Critical).
    /// </summary>
    public LockerEventSeverity Severity { get; set; }

    /// <summary>
    /// Lý do thay đổi trạng thái nếu có.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Thông tin chi tiết phục vụ giám sát và chẩn đoán kỹ thuật.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Thời điểm sự kiện thực tế diễn ra tại nguồn thiết bị.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Thời điểm hệ thống backend tiếp nhận sự kiện.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Tủ locker phát sinh sự kiện.
    /// </summary>
    public Locker Locker { get; set; } = null!;

    /// <summary>
    /// Ngăn locker liên quan nếu có.
    /// </summary>
    public LockerCompartment? LockerCompartment { get; set; }

    /// <summary>
    /// Người dùng gây ra sự kiện nếu có.
    /// </summary>
    public User? ActorUser { get; set; }
}
