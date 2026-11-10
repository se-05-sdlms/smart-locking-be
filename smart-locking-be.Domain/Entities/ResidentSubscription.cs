using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho gói cước mà Cư dân đã đăng ký và sử dụng.
/// </summary>
public sealed class ResidentSubscription
{
    /// <summary>
    /// Mã định danh duy nhất của lượt đăng ký gói cước.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh hồ sơ Cư dân đăng ký gói.
    /// </summary>
    public Guid ResidentProfileId { get; set; }

    /// <summary>
    /// Mã định danh gói cước được đăng ký.
    /// </summary>
    public Guid SubscriptionPlanId { get; set; }

    /// <summary>
    /// Thời điểm bắt đầu hiệu lực của gói cước.
    /// </summary>
    public DateTimeOffset StartDate { get; set; }

    /// <summary>
    /// Thời điểm kết thúc hiệu lực của gói cước.
    /// </summary>
    public DateTimeOffset EndDate { get; set; }

    /// <summary>
    /// Trạng thái của gói cước đăng ký.
    /// </summary>
    public SubscriptionStatus Status { get; set; }

    /// <summary>
    /// Giá tiền của gói cước được lưu vết (snapshot) tại thời điểm đăng ký.
    /// </summary>
    public decimal PriceSnapshot { get; set; }

    // Null means the historical benefit was not recorded; never infer it from today's catalogue.
    public string? PlanCodeSnapshot { get; set; }
    public string? PlanNameSnapshot { get; set; }
    public int? DurationDaysSnapshot { get; set; }
    public int? MaxActiveParcelsSnapshot { get; set; }
    public DateTimeOffset? BenefitsSnapshotRecordedAt { get; set; }

    /// <summary>
    /// Mã đơn hàng trên cổng thanh toán bên thứ ba (PayOS orderCode).
    /// </summary>
    public string? ExternalOrderCode { get; set; }

    /// <summary>
    /// Mã giao dịch trả về từ cổng thanh toán bên thứ ba khi thanh toán thành công.
    /// </summary>
    public string? ExternalTransactionId { get; set; }

    /// <summary>
    /// Thời điểm thanh toán thành công.
    /// </summary>
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>
    /// Thời điểm bản ghi đăng ký được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm bản ghi đăng ký được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Hồ sơ Cư dân sở hữu gói đăng ký này.
    /// </summary>
    public ResidentProfile ResidentProfile { get; set; } = null!;

    /// <summary>
    /// Gói cước đã đăng ký.
    /// </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
}
