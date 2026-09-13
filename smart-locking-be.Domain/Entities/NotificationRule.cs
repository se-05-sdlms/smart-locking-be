using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Quy tắc cấu hình việc phát gửi thông báo theo sự kiện nghiệp vụ, chính sách và kênh gửi.
/// </summary>
public sealed class NotificationRule
{
    /// <summary>
    /// Mã định danh duy nhất của quy tắc thông báo.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh phiên bản chính sách sở hữu quy tắc này.
    /// </summary>
    public Guid SystemPolicyId { get; set; }

    /// <summary>
    /// Mã loại sự kiện nghiệp vụ kích hoạt gửi thông báo (ví dụ: ParcelStored, ParcelOverdue).
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Kênh phát gửi thông báo: InApp, Push, Sms hoặc Email.
    /// </summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>
    /// Số phút gửi trước thời điểm sự kiện/hạn định đối với thông báo nhắc trước; null khi gửi ngay khi sự kiện xảy ra.
    /// </summary>
    public int? LeadTimeMinutes { get; set; }

    /// <summary>
    /// Cho biết quy tắc thông báo hiện đang được bật hay tắt.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Phiên bản chính sách chứa quy tắc thông báo.
    /// </summary>
    public SystemPolicy SystemPolicy { get; set; } = null!;
}
