namespace smart_locking_be.Domain.Entities;

public sealed class ReturnPickupAttempt
{
    public Guid LockerId { get; set; }
    public string ClientKey { get; set; } = string.Empty;
    public int FailedAttempts { get; set; }
    public DateTimeOffset WindowExpiresAt { get; set; }
}
