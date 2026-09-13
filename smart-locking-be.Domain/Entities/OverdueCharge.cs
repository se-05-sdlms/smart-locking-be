using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu thông tin khoản phí quá hạn phát sinh từ kiện hàng lưu trữ vượt thời gian cho phép.
/// </summary>
public sealed class OverdueCharge
{
    /// <summary>
    /// Mã định danh duy nhất của khoản phí quá hạn.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh kiện hàng phát sinh phí quá hạn.
    /// </summary>
    public Guid ParcelId { get; set; }

    /// <summary>
    /// Số tiền phí quá hạn phải thanh toán tính đến thời điểm cập nhật gần nhất.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Mức phí/giờ được snapshot từ chính sách áp dụng tại thời điểm tính phí.
    /// </summary>
    public decimal RatePerHourSnapshot { get; set; }

    /// <summary>
    /// Mã tiền tệ của khoản phí quá hạn.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm bắt đầu tính phí quá hạn.
    /// </summary>
    public DateTimeOffset ChargeStartAt { get; set; }

    /// <summary>
    /// Thời điểm mốc cuối cùng mà khoản phí đã được tính lũy kế tới.
    /// </summary>
    public DateTimeOffset CalculatedThrough { get; set; }

    /// <summary>
    /// Trạng thái thanh toán của khoản phí quá hạn (Outstanding hoặc Paid).
    /// </summary>
    public OverdueChargeStatus Status { get; set; }

    /// <summary>
    /// Thời điểm khoản phí quá hạn được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm khoản phí quá hạn được tính toán/cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Thời điểm khoản phí được xác nhận thanh toán thành công.
    /// </summary>
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>
    /// Kiện hàng phát sinh khoản phí quá hạn.
    /// </summary>
    public Parcel Parcel { get; set; } = null!;

    /// <summary>
    /// Các giao dịch thanh toán được thực hiện cho khoản phí này.
    /// </summary>
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
}
