using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class LockerCommissioning
{
    public Guid LockerId { get; set; }
    public LockerCommissioningStage Stage { get; set; }
    public string? BoundDeviceIdentifier { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? FirmwareHardwareModel { get; set; }
    public string? FirmwareSha256 { get; set; }
    public string? VerifiedConfigurationHash { get; set; }
    public DateTimeOffset? BoundAt { get; set; }
    public DateTimeOffset? ConfiguredAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public DateTimeOffset? HandedOverAt { get; set; }
    public Guid? HandedOverByUserId { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Locker Locker { get; set; } = null!;
}
