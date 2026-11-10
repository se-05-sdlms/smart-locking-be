namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho gói cước dịch vụ sử dụng tủ locker dành cho Cư dân.
/// </summary>
public sealed class SubscriptionPlan
{
    /// <summary>
    /// Mã định danh duy nhất của gói cước.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã code định danh duy nhất của gói cước (ví dụ: MONTHLY_BASIC).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Tên hiển thị của gói cước.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết về quyền lợi và chính sách của gói cước.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Giá tiền của gói cước.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Thời hạn hiệu lực của gói cước tính theo ngày.
    /// </summary>
    public int DurationDays { get; set; }

    /// <summary>
    /// Số lượng kiện hàng tối đa được lưu trữ đồng thời trong locker.
    /// </summary>
    public int MaxActiveParcels { get; set; }

    /// <summary>
    /// Trạng thái gói cước có đang mở cho phép đăng ký hay không.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Thời điểm gói cước được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm gói cước được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Danh sách các lượt đăng ký gói cước này từ cư dân.
    /// </summary>
    public ICollection<ResidentSubscription> ResidentSubscriptions { get; set; } = new List<ResidentSubscription>();
}
