using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho một ngăn vật lý bên trong tủ locker.
/// </summary>
public sealed class LockerCompartment
{
    /// <summary>
    /// Mã định danh duy nhất của ngăn locker.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh tủ locker chứa ngăn này.
    /// </summary>
    public Guid LockerId { get; set; }

    /// <summary>
    /// Mã ngăn hiển thị duy nhất trong phạm vi tủ locker (ví dụ: A01, B02).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái vận hành của ngăn locker, độc lập với việc ngăn đang chứa hàng hay trống.
    /// </summary>
    public LockerCompartmentOperationalStatus OperationalStatus { get; set; }

    /// <summary>
    /// Trạng thái cảm biến cửa gần nhất của ngăn locker.
    /// </summary>
    public DoorStatus DoorStatus { get; set; }

    /// <summary>
    /// Thời điểm thông tin ngăn được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Thời điểm ngăn locker được đăng ký.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Tủ locker chứa ngăn này.
    /// </summary>
    public Locker Locker { get; set; } = null!;

    /// <summary>
    /// Các yêu cầu giao hàng từng được phân bổ vào ngăn locker này.
    /// </summary>
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();

    /// <summary>
    /// Các yêu cầu gửi trả hàng từng được phân bổ vào ngăn locker này.
    /// </summary>
    public ICollection<ReturnRequest> ReturnRequests { get; set; } = new List<ReturnRequest>();

    /// <summary>
    /// Lịch sử các lượt giữ đặt trước ngăn locker này.
    /// </summary>
    public ICollection<CompartmentReservation> Reservations { get; set; } = new List<CompartmentReservation>();

    /// <summary>
    /// Lịch sử các lần truy cập/mở cửa trực tiếp trên ngăn locker này.
    /// </summary>
    public ICollection<LockerAccessEvent> AccessEvents { get; set; } = new List<LockerAccessEvent>();

    /// <summary>
    /// Các sự cố liên quan trực tiếp đến ngăn locker.
    /// </summary>
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    /// <summary>
    /// Các sự kiện vận hành liên quan đến ngăn locker.
    /// </summary>
    public ICollection<LockerEvent> Events { get; set; } = new List<LockerEvent>();

    /// <summary>
    /// Các thao tác mở khóa khẩn cấp nhắm trực tiếp vào ngăn này.
    /// </summary>
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();

    /// <summary>
    /// Các yêu cầu bảo trì liên quan đến ngăn locker này.
    /// </summary>
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
