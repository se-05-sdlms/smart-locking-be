namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu thông tin Refresh Token đã mã hóa hash dùng cho xác thực JWT và hỗ trợ thu hồi token.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>
    /// Mã định danh duy nhất của bản ghi Refresh Token.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh người dùng sở hữu Refresh Token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Giá trị băm (hash) của Refresh Token; không lưu token dạng rõ.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm Refresh Token hết hiệu lực.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Thời điểm phát hành Refresh Token.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm Refresh Token bị thu hồi; null nếu token còn hiệu lực.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Địa chỉ IP của client tại thời điểm token được phát hành.
    /// </summary>
    public string? CreatedByIp { get; set; }

    /// <summary>
    /// Địa chỉ IP của client tại thời điểm token bị thu hồi.
    /// </summary>
    public string? RevokedByIp { get; set; }

    /// <summary>
    /// Mã định danh của Refresh Token mới thay thế token này trong cơ chế xoay vòng token (rotation); null nếu chưa bị thay thế.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>
    /// Tài khoản người dùng sở hữu token.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// Refresh Token mới đã thay thế token này khi xoay vòng token.
    /// </summary>
    public RefreshToken? ReplacedByToken { get; set; }

    /// <summary>
    /// Danh sách các Refresh Token cũ được thay thế bởi token này.
    /// </summary>
    public ICollection<RefreshToken> ReplacementForTokens { get; set; } = new List<RefreshToken>();
}
