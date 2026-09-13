using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Yêu cầu bảo trì tủ locker hoặc ngăn locker do Nhân viên vận hành tạo và theo dõi.
/// </summary>
public sealed class MaintenanceRequest
{
    /// <summary>
    /// Mã định danh duy nhất của yêu cầu bảo trì.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh tủ locker cần bảo trì.
    /// </summary>
    public Guid LockerId { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker cụ thể cần bảo trì nếu có.
    /// </summary>
    public Guid? LockerCompartmentId { get; set; }

    /// <summary>
    /// Mã định danh Nhân viên vận hành đã tạo yêu cầu bảo trì.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// Mã định danh sự cố nguồn dẫn đến yêu cầu bảo trì này nếu có.
    /// </summary>
    public Guid? IncidentId { get; set; }

    /// <summary>
    /// Mức độ ưu tiên xử lý bảo trì (Low, Normal, High, Critical).
    /// </summary>
    public MaintenancePriority Priority { get; set; }

    /// <summary>
    /// Trạng thái vòng đời yêu cầu bảo trì: Open, InProgress, Closed hoặc Cancelled.
    /// </summary>
    public MaintenanceStatus Status { get; set; }

    /// <summary>
    /// Mô tả chi tiết lỗi hoặc nội dung công việc cần bảo trì.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Tóm tắt kết quả xử lý bảo trì khi công việc hoàn thành và đóng request.
    /// </summary>
    public string? ResolutionSummary { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu bảo trì được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu bảo trì được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu bảo trì được đóng.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Tủ locker cần bảo trì.
    /// </summary>
    public Locker Locker { get; set; } = null!;

    /// <summary>
    /// Ngăn locker cần bảo trì nếu có.
    /// </summary>
    public LockerCompartment? LockerCompartment { get; set; }

    /// <summary>
    /// Nhân viên vận hành đã tạo yêu cầu bảo trì.
    /// </summary>
    public User CreatedByUser { get; set; } = null!;

    /// <summary>
    /// Sự cố nguồn liên quan nếu có.
    /// </summary>
    public Incident? Incident { get; set; }

    /// <summary>
    /// Timeline các hoạt động cập nhật trên yêu cầu bảo trì.
    /// </summary>
    public ICollection<MaintenanceActivity> Activities { get; set; } = new List<MaintenanceActivity>();
}
