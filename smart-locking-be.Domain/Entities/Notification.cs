using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu thông báo đã được tạo để gửi cho người dùng qua kênh tương ứng.
/// </summary>
public sealed class Notification
{
    /// <summary>
    /// Mã định danh duy nhất của thông báo.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh người nhận thông báo.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Mã loại thông báo phục vụ phân loại và hiển thị giao diện.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Kênh phát gửi thông báo: InApp, Push, Sms hoặc Email.
    /// </summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>
    /// Tiêu đề thông báo.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung chi tiết của thông báo.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Mã định danh kiện hàng liên quan nếu thông báo phát sinh từ kiện hàng.
    /// </summary>
    public Guid? ParcelId { get; set; }

    /// <summary>
    /// Mã định danh sự cố liên quan nếu thông báo phát sinh từ sự cố.
    /// </summary>
    public Guid? IncidentId { get; set; }

    /// <summary>
    /// Mã định danh giao dịch thanh toán liên quan nếu thông báo phát sinh từ thanh toán.
    /// </summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>
    /// Trạng thái phát gửi thông báo: Pending, Sent hoặc Failed.
    /// </summary>
    public NotificationDeliveryStatus DeliveryStatus { get; set; }

    /// <summary>
    /// Trạng thái đã đọc của thông báo trong ứng dụng đối với người dùng.
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// Thời điểm thông báo được phát gửi thành công qua kênh tương ứng.
    /// </summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>
    /// Thời điểm người dùng đánh dấu hoặc đọc thông báo.
    /// </summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// Thời điểm bản ghi thông báo được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Người dùng nhận thông báo.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// Kiện hàng liên quan nếu có.
    /// </summary>
    public Parcel? Parcel { get; set; }

    /// <summary>
    /// Sự cố liên quan nếu có.
    /// </summary>
    public Incident? Incident { get; set; }

    /// <summary>
    /// Giao dịch thanh toán liên quan nếu có.
    /// </summary>
    public PaymentTransaction? PaymentTransaction { get; set; }
}
