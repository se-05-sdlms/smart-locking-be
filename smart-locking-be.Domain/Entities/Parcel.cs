using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho kiện hàng đã được gửi thành công vào ngăn locker.
/// </summary>
public sealed class Parcel
{
    /// <summary>
    /// Mã định danh duy nhất của kiện hàng.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh yêu cầu giao hàng đã tạo ra kiện hàng này.
    /// </summary>
    public Guid DeliveryRequestId { get; set; }

    /// <summary>
    /// Mã kiện hàng duy nhất dùng cho tra cứu và theo dõi lịch sử.
    /// </summary>
    public string ParcelCode { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái vòng đời hiện tại của kiện hàng: Stored, Overdue, Retrieved hoặc Removed.
    /// </summary>
    public ParcelStatus Status { get; set; }

    /// <summary>
    /// Thời điểm kiện hàng được xác nhận đã đặt an toàn trong ngăn locker.
    /// </summary>
    public DateTimeOffset StoredAt { get; set; }

    /// <summary>
    /// Thời điểm kiện hàng bắt đầu chuyển sang trạng thái quá hạn theo chính sách.
    /// </summary>
    public DateTimeOffset PickupDueAt { get; set; }

    /// <summary>
    /// Thời điểm vượt quá hạn mức lưu trữ tối đa cho phép trong locker.
    /// </summary>
    public DateTimeOffset MaxStorageUntil { get; set; }

    /// <summary>
    /// Thời điểm Cư dân lấy kiện hàng thành công; null nếu chưa lấy.
    /// </summary>
    public DateTimeOffset? RetrievedAt { get; set; }

    /// <summary>
    /// Thời điểm Nhân viên vận hành tịch thu hoặc giải phóng kiện hàng khỏi ngăn; null nếu chưa tịch thu.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>
    /// Mã định danh Nhân viên vận hành đã thực hiện tịch thu kiện hàng; null nếu chưa tịch thu.
    /// </summary>
    public Guid? RemovedByUserId { get; set; }

    /// <summary>
    /// Ghi chú lý do tịch thu hoặc giải phóng kiện hàng.
    /// </summary>
    public string? RemovalReason { get; set; }

    /// <summary>
    /// Thời điểm bản ghi kiện hàng được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm thông tin kiện hàng được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Yêu cầu giao hàng tạo ra kiện hàng này.
    /// </summary>
    public DeliveryRequest DeliveryRequest { get; set; } = null!;

    /// <summary>
    /// Nhân viên vận hành đã thực hiện tịch thu kiện hàng nếu có.
    /// </summary>
    public User? RemovedByUser { get; set; }

    /// <summary>
    /// Danh sách mã OTP thử thách liên quan tới kiện hàng.
    /// </summary>
    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();

    /// <summary>
    /// Lịch sử thay đổi trạng thái của kiện hàng.
    /// </summary>
    public ICollection<ParcelStatusHistory> StatusHistory { get; set; } = new List<ParcelStatusHistory>();

    /// <summary>
    /// Lịch sử các lần thử hoặc thực hiện xác thực lấy kiện hàng.
    /// </summary>
    public ICollection<LockerAccessEvent> AccessEvents { get; set; } = new List<LockerAccessEvent>();

    /// <summary>
    /// Các yêu cầu gửi trả hàng được khởi tạo từ kiện hàng gốc này nếu có.
    /// </summary>
    public ICollection<ReturnRequest> ReturnRequests { get; set; } = new List<ReturnRequest>();

    /// <summary>
    /// Khoản phí quá hạn hiện hành của kiện hàng nếu phát sinh.
    /// </summary>
    public OverdueCharge? OverdueCharge { get; set; }

    /// <summary>
    /// Danh sách thông báo liên quan tới kiện hàng.
    /// </summary>
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    /// <summary>
    /// Danh sách sự cố liên quan tới kiện hàng.
    /// </summary>
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
