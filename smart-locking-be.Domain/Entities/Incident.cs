using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Quản lý sự cố phát sinh do Cư dân, Shipper hoặc Hệ thống báo cáo và do Nhân viên vận hành xử lý.
/// </summary>
public sealed class Incident
{
    /// <summary>
    /// Mã định danh duy nhất của sự cố.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh người dùng báo cáo sự cố nếu đã đăng nhập; null với Shipper khách hoặc hệ thống tự động.
    /// </summary>
    public Guid? ReporterUserId { get; set; }

    /// <summary>
    /// Họ tên người báo cáo dùng cho khách chưa đăng nhập.
    /// </summary>
    public string? ReporterName { get; set; }

    /// <summary>
    /// Số điện thoại liên hệ của người báo cáo sự cố.
    /// </summary>
    public string? ReporterPhone { get; set; }

    /// <summary>
    /// Mã định danh yêu cầu giao hàng liên quan nếu sự cố xảy ra khi gửi hàng.
    /// </summary>
    public Guid? DeliveryRequestId { get; set; }

    /// <summary>
    /// Mã định danh kiện hàng liên quan nếu sự cố phát sinh từ kiện hàng.
    /// </summary>
    public Guid? ParcelId { get; set; }

    /// <summary>
    /// Mã định danh tủ locker liên quan đến sự cố.
    /// </summary>
    public Guid? LockerId { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker liên quan đến sự cố.
    /// </summary>
    public Guid? LockerCompartmentId { get; set; }

    /// <summary>
    /// Mã định danh giao dịch thanh toán liên quan nếu sự cố về thanh toán.
    /// </summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>
    /// Mã định danh Nhân viên vận hành đang được giao xử lý sự cố.
    /// </summary>
    public Guid? AssignedOperatorUserId { get; set; }

    /// <summary>
    /// Mã loại sự cố.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Nguồn phát sinh sự cố: Resident, Shipper hoặc System.
    /// </summary>
    public IncidentSource Source { get; set; }

    /// <summary>
    /// Trạng thái xử lý sự cố: Open, Investigating, Resolved hoặc Escalated.
    /// </summary>
    public IncidentStatus Status { get; set; }

    /// <summary>
    /// Tiêu đề tóm tắt sự cố.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết vấn đề hoặc sự cố.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Tóm tắt kết quả và phương án xử lý khi sự cố được giải quyết.
    /// </summary>
    public string? ResolutionSummary { get; set; }

    /// <summary>
    /// Thời điểm sự cố được chuyển cấp xử lý cho Quản trị viên.
    /// </summary>
    public DateTimeOffset? EscalatedAt { get; set; }

    /// <summary>
    /// Thời điểm sự cố được giải quyết thành công.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>
    /// Thời điểm bản ghi sự cố được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm sự cố được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Người dùng báo cáo sự cố nếu đã đăng nhập.
    /// </summary>
    public User? ReporterUser { get; set; }

    /// <summary>
    /// Yêu cầu giao hàng liên quan nếu có.
    /// </summary>
    public DeliveryRequest? DeliveryRequest { get; set; }

    /// <summary>
    /// Kiện hàng liên quan nếu có.
    /// </summary>
    public Parcel? Parcel { get; set; }

    /// <summary>
    /// Tủ locker liên quan nếu có.
    /// </summary>
    public Locker? Locker { get; set; }

    /// <summary>
    /// Ngăn locker liên quan nếu có.
    /// </summary>
    public LockerCompartment? LockerCompartment { get; set; }

    /// <summary>
    /// Giao dịch thanh toán liên quan nếu có.
    /// </summary>
    public PaymentTransaction? PaymentTransaction { get; set; }

    /// <summary>
    /// Nhân viên vận hành được phân công xử lý sự cố.
    /// </summary>
    public User? AssignedOperatorUser { get; set; }

    /// <summary>
    /// Các thông báo liên quan đến sự cố.
    /// </summary>
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    /// <summary>
    /// Timeline các hành động xử lý trên sự cố.
    /// </summary>
    public ICollection<IncidentAction> Actions { get; set; } = new List<IncidentAction>();

    /// <summary>
    /// Các thao tác mở khóa khẩn cấp xuất phát từ sự cố này.
    /// </summary>
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();

    /// <summary>
    /// Các yêu cầu bảo trì được khởi tạo từ sự cố này.
    /// </summary>
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
