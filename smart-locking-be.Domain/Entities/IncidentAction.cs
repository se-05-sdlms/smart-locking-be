using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu vết từng hành động kiểm tra, xử lý hoặc chuyển trạng thái của sự cố.
/// </summary>
public sealed class IncidentAction
{
    /// <summary>
    /// Mã định danh duy nhất của hành động xử lý sự cố.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh sự cố được thao tác.
    /// </summary>
    public Guid IncidentId { get; set; }

    /// <summary>
    /// Mã định danh người dùng / Nhân viên thực hiện hành động.
    /// </summary>
    public Guid ActionByUserId { get; set; }

    /// <summary>
    /// Mã loại hành động đã thực hiện.
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái sự cố trước khi thực hiện hành động nếu có chuyển trạng thái.
    /// </summary>
    public IncidentStatus? FromStatus { get; set; }

    /// <summary>
    /// Trạng thái sự cố sau khi thực hiện hành động nếu có chuyển trạng thái.
    /// </summary>
    public IncidentStatus? ToStatus { get; set; }

    /// <summary>
    /// Ghi chú nội dung chi tiết của người xử lý.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Thời điểm hành động được ghi nhận.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Sự cố sở hữu hành động này.
    /// </summary>
    public Incident Incident { get; set; } = null!;

    /// <summary>
    /// Người dùng thực hiện hành động.
    /// </summary>
    public User ActionByUser { get; set; } = null!;
}
