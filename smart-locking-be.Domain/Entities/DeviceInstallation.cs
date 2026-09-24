namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Thiết bị đã đăng ký nhận push notification của một người dùng.
/// </summary>
public sealed class DeviceInstallation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string InstallationId { get; set; } = string.Empty;

    public string ExpoPushToken { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
