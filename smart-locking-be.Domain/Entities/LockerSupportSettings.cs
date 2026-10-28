namespace smart_locking_be.Domain.Entities;

public sealed class LockerSupportSettings
{
    public Guid LockerId { get; set; }
    public string? FallbackName { get; set; }
    public string? FallbackPhoneNumber { get; set; }
    public string FallbackInstructions { get; set; } = string.Empty;
    public string? FallbackHours { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
