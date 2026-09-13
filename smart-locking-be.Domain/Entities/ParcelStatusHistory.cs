using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu lịch sử toàn bộ các lần chuyển đổi trạng thái của kiện hàng.
/// </summary>
public sealed class ParcelStatusHistory
{
    /// <summary>
    /// Mã định danh duy nhất của bản ghi lịch sử trạng thái.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh kiện hàng được thay đổi trạng thái.
    /// </summary>
    public Guid ParcelId { get; set; }

    /// <summary>
    /// Trạng thái kiện hàng trước khi thay đổi; null khi ghi nhận trạng thái đầu tiên.
    /// </summary>
    public ParcelStatus? FromStatus { get; set; }

    /// <summary>
    /// Trạng thái kiện hàng sau khi thay đổi.
    /// </summary>
    public ParcelStatus ToStatus { get; set; }

    /// <summary>
    /// Lý do hoặc ngữ cảnh thay đổi trạng thái nếu cần ghi giải thích.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Mã định danh người dùng thực hiện thay đổi; null nếu do hệ thống tự động chuyển state.
    /// </summary>
    public Guid? ChangedByUserId { get; set; }

    /// <summary>
    /// Thời điểm trạng thái được thay đổi.
    /// </summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>
    /// Kiện hàng sở hữu bản ghi lịch sử.
    /// </summary>
    public Parcel Parcel { get; set; } = null!;

    /// <summary>
    /// Người dùng thực hiện thay đổi trạng thái nếu có.
    /// </summary>
    public User? ChangedByUser { get; set; }
}
