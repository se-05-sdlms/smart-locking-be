using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu thông tin thử thách xác thực OTP cho đăng ký, reset mật khẩu hoặc lấy kiện hàng.
/// </summary>
public sealed class OtpChallenge
{
    /// <summary>
    /// Mã định danh của thử thách OTP.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh người dùng liên quan; có thể null khi đăng ký trước khi tạo tài khoản.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Mã định danh kiện hàng liên quan nếu OTP dùng để lấy hàng; null với các mục đích khác.
    /// </summary>
    public Guid? ParcelId { get; set; }

    /// <summary>
    /// Số điện thoại đích nhận mã OTP được snapshot tại thời điểm phát hành.
    /// </summary>
    public string DestinationPhone { get; set; } = string.Empty;

    /// <summary>
    /// Mục đích sử dụng của mã OTP để tránh dùng chéo giữa các workflow.
    /// </summary>
    public OtpPurpose Purpose { get; set; }

    /// <summary>
    /// Giá trị băm của mã OTP; tuyệt đối không lưu OTP dạng rõ.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm mã OTP hết hiệu lực.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Thời điểm mã OTP được sử dụng xác thực thành công; null nếu chưa dùng.
    /// </summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>
    /// Thời điểm mã OTP bị vô hiệu hóa trước hạn; null nếu chưa thu hồi.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Số lần xác thực thất bại đã ghi nhận cho thử thách này.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Thời điểm hết hạn khóa tạm thời lượt thử OTP sau khi nhập sai vượt quá số lần quy định; null khi không bị khóa.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// Thời điểm thử thách OTP được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Tài khoản người dùng liên quan.
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Kiện hàng liên quan đến mã OTP lấy hàng.
    /// </summary>
    public Parcel? Parcel { get; set; }
}
