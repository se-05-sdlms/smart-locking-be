namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu thông tin tham chiếu mẫu nhận diện sinh trắc học (biometric template) của Cư dân.
/// </summary>
public sealed class ResidentBiometric
{
    /// <summary>
    /// Mã định danh bản ghi sinh trắc học.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh hồ sơ Cư dân sở hữu dữ liệu sinh trắc học.
    /// </summary>
    public Guid ResidentProfileId { get; set; }

    /// <summary>
    /// Chuỗi tham chiếu tới template sinh trắc học lưu trữ an toàn; không lưu ảnh khuôn mặt thô.
    /// </summary>
    public string TemplateReference { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm dữ liệu sinh trắc học được đăng ký.
    /// </summary>
    public DateTimeOffset EnrolledAt { get; set; }

    /// <summary>
    /// Thời điểm dữ liệu sinh trắc học bị vô hiệu hóa/thu hồi; null khi còn hiệu lực.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Thời điểm thông tin sinh trắc học được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Hồ sơ Cư dân sở hữu bản ghi sinh trắc học.
    /// </summary>
    public ResidentProfile ResidentProfile { get; set; } = null!;
}
