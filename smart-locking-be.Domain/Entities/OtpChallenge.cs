using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class OtpChallenge
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ParcelId { get; set; }
    public string DestinationPhone { get; set; } = string.Empty;
    public OtpPurpose Purpose { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
    public Parcel? Parcel { get; set; }
}
