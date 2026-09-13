using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Nhật ký kiểm toán (Audit Trail) bất biến lưu vết các hành động đăng nhập, thay đổi cấu hình, phân quyền, mở khóa khẩn cấp và bảo mật.
/// </summary>
public sealed class AuditLog
{
    /// <summary>
    /// Mã định danh duy nhất của bản ghi nhật ký kiểm toán.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh người dùng thực hiện thao tác; null khi tác nhân là hệ thống hoặc khách chưa đăng nhập.
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Mã hoặc tên thao tác được ghi nhận kiểm toán.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Loại thực thể hoặc tài nguyên chịu tác động.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Mã định danh thực thể hoặc tài nguyên chịu tác động nếu có.
    /// </summary>
    public Guid? EntityId { get; set; }

    /// <summary>
    /// Kết quả của thao tác được kiểm toán (Succeeded hoặc Failed).
    /// </summary>
    public AuditLogResult Result { get; set; }

    /// <summary>
    /// Địa chỉ IP của tác nhân thực hiện thao tác nếu có.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Chi tiết bổ sung phục vụ điều tra và tuân thủ quy định bảo mật.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Thời điểm thực tế diễn ra thao tác được kiểm toán.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Người dùng thực hiện thao tác nếu có.
    /// </summary>
    public User? ActorUser { get; set; }
}
