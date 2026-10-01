using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Hồ sơ nghiệp vụ riêng của Cư dân (Resident), tách biệt khỏi thông tin đăng nhập chung.
/// </summary>
public sealed class ResidentProfile
{
    /// <summary>
    /// Mã định danh hồ sơ Cư dân.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh tài khoản người dùng gắn 1-1 với hồ sơ Cư dân.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Mã tủ Locker duy nhất Cư dân đăng ký sử dụng. Có thể null với dữ liệu cũ chưa hoàn tất onboarding.
    /// </summary>
    public Guid? RegisteredLockerId { get; set; }

    /// <summary>
    /// Họ tên đầy đủ của Cư dân dùng trong hiển thị và nghiệp vụ giao nhận.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Ngày sinh của Cư dân nếu được cung cấp.
    /// </summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// Đường dẫn ảnh đại diện của Cư dân.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Chế độ quyết định việc yêu cầu giao hàng được tự động duyệt hay cần Cư dân duyệt thủ công.
    /// </summary>
    public DeliveryApprovalMode DeliveryApprovalMode { get; set; }

    /// <summary>
    /// Cho biết Cư dân đã bật phương thức nhận hàng bằng nhận diện khuôn mặt hay chưa.
    /// </summary>
    public bool FaceRecognitionEnabled { get; set; }

    /// <summary>
    /// Thời điểm hồ sơ Cư dân được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm hồ sơ Cư dân được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Tài khoản người dùng gắn 1-1 với hồ sơ Cư dân.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// Tủ Locker duy nhất Cư dân đăng ký sử dụng.
    /// </summary>
    public Locker? RegisteredLocker { get; set; }

    /// <summary>
    /// Các bản ghi dữ liệu sinh trắc học của Cư dân.
    /// </summary>
    public ICollection<ResidentBiometric> Biometrics { get; set; } = new List<ResidentBiometric>();

    /// <summary>
    /// Các yêu cầu giao hàng gửi tới Cư dân này.
    /// </summary>
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();

    /// <summary>
    /// Các yêu cầu gửi trả hàng do Cư dân này tạo.
    /// </summary>
    public ICollection<ReturnRequest> ReturnRequests { get; set; } = new List<ReturnRequest>();
}
