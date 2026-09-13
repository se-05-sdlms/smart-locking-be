using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Phiên bản chính sách hệ thống quy định các thời hạn timeout, mức phí, giới hạn lưu trữ và cấu hình tính năng.
/// </summary>
public sealed class SystemPolicy
{
    /// <summary>
    /// Mã định danh của phiên bản chính sách.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Số phiên bản chính sách tăng dần và duy nhất.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Chế độ phê duyệt mặc định cho yêu cầu giao hàng.
    /// </summary>
    public DeliveryApprovalMode DefaultApprovalMode { get; set; }

    /// <summary>
    /// Số phút không hoạt động tối đa trước khi phiên giao hàng của Shipper (guest session) hết hạn.
    /// </summary>
    public int GuestSessionTimeoutMinutes { get; set; }

    /// <summary>
    /// Số phút Cư dân có thể phản hồi yêu cầu phê duyệt giao hàng thủ công.
    /// </summary>
    public int ManualApprovalTimeoutMinutes { get; set; }

    /// <summary>
    /// Số phút ngăn locker được giữ đặt trước cho một yêu cầu giao hàng sau khi phân bổ.
    /// </summary>
    public int CompartmentReservationMinutes { get; set; }

    /// <summary>
    /// Số giờ sau khi gửi hàng bắt đầu tính trạng thái và phí quá hạn.
    /// </summary>
    public int OverdueStartAfterHours { get; set; }

    /// <summary>
    /// Mức phí quá hạn tính cho mỗi giờ lưu trữ vượt quá thời hạn quy định.
    /// </summary>
    public decimal OverdueFeePerHour { get; set; }

    /// <summary>
    /// Mã tiền tệ áp dụng cho phí quá hạn (ví dụ: VND).
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian tối đa (tính theo giờ) kiện hàng được phép lưu trữ trong locker trước khi đưa vào quy trình tịch thu/clearance.
    /// </summary>
    public int MaxStorageHours { get; set; }

    /// <summary>
    /// Số giờ lưu trữ tối thiểu tính từ lúc gửi để kiện hàng đủ điều kiện cho Nhân viên vận hành tịch thu.
    /// </summary>
    public int ClearanceEligibilityAfterHours { get; set; }

    /// <summary>
    /// Số giờ gửi thông báo trước cho Cư dân trước khi kiện hàng bị tịch thu.
    /// </summary>
    public int ClearanceNoticeBeforeHours { get; set; }

    /// <summary>
    /// Số lần nhập mã OTP sai tối đa cho phép trước khi tạm thời khóa xác thực.
    /// </summary>
    public int OtpMaxAttempts { get; set; }

    /// <summary>
    /// Số phút khóa tạm thời lượt xác thực OTP sau khi nhập sai quá số lần cho phép.
    /// </summary>
    public int OtpLockoutMinutes { get; set; }

    /// <summary>
    /// Cho phép lấy hàng bằng mã QR cá nhân của Cư dân.
    /// </summary>
    public bool EnablePersonalQr { get; set; }

    /// <summary>
    /// Cho phép lấy hàng bằng mã OTP.
    /// </summary>
    public bool EnableOtp { get; set; }

    /// <summary>
    /// Cho phép mở ngăn lấy hàng từ xa qua ứng dụng di động.
    /// </summary>
    public bool EnableRemoteUnlock { get; set; }

    /// <summary>
    /// Cho phép lấy hàng bằng nhận diện khuôn mặt nếu Cư dân đã đăng ký.
    /// </summary>
    public bool EnableFaceRecognition { get; set; }

    /// <summary>
    /// Thời điểm chính sách bắt đầu có hiệu lực.
    /// </summary>
    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>
    /// Thời điểm chính sách hết hiệu lực; null khi chưa ấn định thời điểm kết thúc.
    /// </summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>
    /// Cho biết đây có phải chính sách hiện hành đang được áp dụng cho các phiên giao hàng mới hay không.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Mã định danh Quản trị viên đã tạo phiên bản chính sách.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// Thời điểm phiên bản chính sách được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Quản trị viên đã tạo chính sách.
    /// </summary>
    public User CreatedByUser { get; set; } = null!;

    /// <summary>
    /// Các quy tắc gửi thông báo thuộc phiên bản chính sách này.
    /// </summary>
    public ICollection<NotificationRule> NotificationRules { get; set; } = new List<NotificationRule>();

    /// <summary>
    /// Các yêu cầu giao hàng đã snapshot phiên bản chính sách này.
    /// </summary>
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();
}
