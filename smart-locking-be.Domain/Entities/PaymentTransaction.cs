using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu chi tiết từng lượt giao dịch thanh toán kết nối với cổng thanh toán cho phí quá hạn.
/// </summary>
public sealed class PaymentTransaction
{
    /// <summary>
    /// Mã định danh giao dịch thanh toán nội bộ.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh khoản phí quá hạn được thanh toán.
    /// </summary>
    public Guid OverdueChargeId { get; set; }

    /// <summary>
    /// Mã order giao dịch PayOS để đối chiếu request và webhook callback.
    /// </summary>
    public string? ExternalOrderCode { get; set; }

    /// <summary>
    /// Mã giao dịch bên ngoài do PayOS cung cấp; null trước khi cổng thanh toán phản hồi thành công.
    /// </summary>
    public string? ExternalTransactionId { get; set; }

    /// <summary>
    /// Số tiền thực hiện giao dịch thanh toán.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Mã tiền tệ của giao dịch.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái của giao dịch thanh toán: Pending, Success, Failed hoặc Cancelled.
    /// </summary>
    public PaymentTransactionStatus Status { get; set; }

    /// <summary>
    /// Lý do thất bại nếu giao dịch thanh toán không thành công.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Thời điểm khởi tạo yêu cầu thanh toán.
    /// </summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>
    /// Thời điểm giao dịch kết thúc (thành công, thất bại hoặc hủy).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Khoản phí quá hạn được thanh toán bởi giao dịch này.
    /// </summary>
    public OverdueCharge OverdueCharge { get; set; } = null!;

    /// <summary>
    /// Các thông báo được tạo liên quan tới kết quả thanh toán.
    /// </summary>
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    /// <summary>
    /// Các sự cố phát sinh liên quan tới giao dịch thanh toán.
    /// </summary>
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
