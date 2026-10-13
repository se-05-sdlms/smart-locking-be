namespace smart_locking_be.Domain.Enums;

/// <summary>
/// Trạng thái của gói cước đăng ký của Cư dân.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>
    /// Đang chờ thanh toán phí gói cước.
    /// </summary>
    PendingPayment,

    /// <summary>
    /// Gói cước đang có hiệu lực.
    /// </summary>
    Active,

    /// <summary>
    /// Gói cước đã hết hạn sử dụng.
    /// </summary>
    Expired,

    /// <summary>
    /// Gói cước đã bị hủy bởi người dùng hoặc hệ thống.
    /// </summary>
    Cancelled
}
