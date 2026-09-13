using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho một tủ locker vật lý/thiết bị IoT thuộc cụm locker.
/// </summary>
public sealed class Locker
{
    /// <summary>
    /// Mã định danh duy nhất của locker.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh cụm locker chứa tủ locker này.
    /// </summary>
    public Guid LockerClusterId { get; set; }

    /// <summary>
    /// Mã tủ locker duy nhất trong phạm vi cụm locker.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Mã định danh phần cứng/IoT của thiết bị dùng để định tuyến tin nhắn và lệnh điều khiển.
    /// </summary>
    public string DeviceIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái vận hành của locker do hệ thống hoặc Nhân viên quản lý.
    /// </summary>
    public LockerOperationalStatus OperationalStatus { get; set; }

    /// <summary>
    /// Trạng thái kết nối mạng hiện tại của thiết bị locker.
    /// </summary>
    public LockerConnectionStatus ConnectionStatus { get; set; }

    /// <summary>
    /// Thời điểm gần nhất hệ thống nhận tín hiệu heartbeat/sự kiện từ thiết bị locker.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// Thời điểm tủ locker được đăng ký vào hệ thống.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm thông tin locker được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Cụm locker chứa tủ locker này.
    /// </summary>
    public LockerCluster LockerCluster { get; set; } = null!;

    /// <summary>
    /// Danh sách các ngăn vật lý thuộc tủ locker.
    /// </summary>
    public ICollection<LockerCompartment> Compartments { get; set; } = new List<LockerCompartment>();

    /// <summary>
    /// Các phân công Nhân viên vận hành trực tiếp quản lý locker này.
    /// </summary>
    public ICollection<OperatorAssignment> OperatorAssignments { get; set; } = new List<OperatorAssignment>();

    /// <summary>
    /// Các sự cố (incident) liên quan tới tủ locker.
    /// </summary>
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    /// <summary>
    /// Lịch sử sự kiện vận hành và telemetry của tủ locker.
    /// </summary>
    public ICollection<LockerEvent> Events { get; set; } = new List<LockerEvent>();

    /// <summary>
    /// Các thao tác mở khóa khẩn cấp áp dụng cho tủ locker.
    /// </summary>
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();

    /// <summary>
    /// Lịch sử các lượt mở tủ (locker access events) diễn ra tại tủ locker này.
    /// </summary>
    public ICollection<LockerAccessEvent> AccessEvents { get; set; } = new List<LockerAccessEvent>();

    /// <summary>
    /// Các yêu cầu bảo trì đối với tủ locker.
    /// </summary>
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
