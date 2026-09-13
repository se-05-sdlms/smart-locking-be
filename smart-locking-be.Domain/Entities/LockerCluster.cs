using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Nhóm các tủ locker vật lý nằm trong cùng một tòa nhà hoặc khu vực vận hành.
/// </summary>
public sealed class LockerCluster
{
    /// <summary>
    /// Mã định danh của cụm locker.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh tòa nhà chứa cụm locker.
    /// </summary>
    public Guid BuildingId { get; set; }

    /// <summary>
    /// Mã cụm locker duy nhất trong phạm vi tòa nhà.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Tên hiển thị của cụm locker.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết vị trí lắp đặt cụm locker trong tòa nhà.
    /// </summary>
    public string? LocationDescription { get; set; }

    /// <summary>
    /// Trạng thái hoạt động hành chính của cụm locker.
    /// </summary>
    public LockerClusterStatus Status { get; set; }

    /// <summary>
    /// Thời điểm cụm locker được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm cụm locker được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Tòa nhà chứa cụm locker.
    /// </summary>
    public Building Building { get; set; } = null!;

    /// <summary>
    /// Danh sách tủ locker thuộc cụm.
    /// </summary>
    public ICollection<Locker> Lockers { get; set; } = new List<Locker>();

    /// <summary>
    /// Danh sách các yêu cầu giao hàng thực hiện tại cụm locker này.
    /// </summary>
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();

    /// <summary>
    /// Danh sách các yêu cầu gửi trả hàng thực hiện tại cụm locker này.
    /// </summary>
    public ICollection<ReturnRequest> ReturnRequests { get; set; } = new List<ReturnRequest>();

    /// <summary>
    /// Danh sách phân công Nhân viên vận hành trực tiếp quản lý cụm locker.
    /// </summary>
    public ICollection<OperatorAssignment> OperatorAssignments { get; set; } = new List<OperatorAssignment>();
}
