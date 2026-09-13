using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu thông tin nhật ký hoạt động/tiến độ trên yêu cầu bảo trì.
/// </summary>
public sealed class MaintenanceActivity
{
    /// <summary>
    /// Mã định danh duy nhất của hoạt động bảo trì.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh yêu cầu bảo trì được cập nhật.
    /// </summary>
    public Guid MaintenanceRequestId { get; set; }

    /// <summary>
    /// Mã định danh người dùng / Kỹ thuật viên thực hiện hoạt động.
    /// </summary>
    public Guid ActionByUserId { get; set; }

    /// <summary>
    /// Mã loại hoạt động đã thực hiện.
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái yêu cầu bảo trì trước hoạt động nếu có chuyển trạng thái.
    /// </summary>
    public MaintenanceStatus? FromStatus { get; set; }

    /// <summary>
    /// Trạng thái yêu cầu bảo trì sau hoạt động nếu có chuyển trạng thái.
    /// </summary>
    public MaintenanceStatus? ToStatus { get; set; }

    /// <summary>
    /// Ghi chú thông tin chi tiết về hoạt động bảo trì.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Thời điểm hoạt động bảo trì được ghi nhận.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Yêu cầu bảo trì sở hữu hoạt động này.
    /// </summary>
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;

    /// <summary>
    /// Người dùng thực hiện hoạt động bảo trì.
    /// </summary>
    public User ActionByUser { get; set; } = null!;
}
