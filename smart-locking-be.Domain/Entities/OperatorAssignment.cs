namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu thông tin phân công phạm vi quản lý vận hành tủ locker cho Nhân viên vận hành (Locker Operator).
/// </summary>
public sealed class OperatorAssignment
{
    /// <summary>
    /// Mã định danh của bản ghi phân công.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh người dùng mang vai trò Locker Operator được phân công.
    /// </summary>
    public Guid OperatorUserId { get; set; }

    /// <summary>
    /// Mã định danh tủ locker được phân công quản lý.
    /// </summary>
    public Guid LockerId { get; set; }

    /// <summary>
    /// Mã định danh Quản trị viên đã thực hiện tạo phân công.
    /// </summary>
    public Guid AssignedByUserId { get; set; }

    /// <summary>
    /// Thời điểm phân công bắt đầu có hiệu lực.
    /// </summary>
    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>
    /// Thời điểm phân công bị thu hồi; null khi phân công còn hiệu lực.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Ghi chú lý do phân công, thay đổi hoặc thu hồi.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Tài khoản Nhân viên vận hành được phân công.
    /// </summary>
    public User OperatorUser { get; set; } = null!;

    /// <summary>
    /// Tủ locker được giao quản lý.
    /// </summary>
    public Locker Locker { get; set; } = null!;

    /// <summary>
    /// Quản trị viên đã thực hiện phân công.
    /// </summary>
    public User AssignedByUser { get; set; } = null!;
}
