using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class EmergencyUnlock
{
    public Guid Id { get; set; }
    public Guid OperatorUserId { get; set; }
    public Guid LockerId { get; set; }
    public Guid? LockerCompartmentId { get; set; }
    public Guid? IncidentId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public EmergencyUnlockResult Result { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public User OperatorUser { get; set; } = null!;
    public Locker Locker { get; set; } = null!;
    public LockerCompartment? LockerCompartment { get; set; }
    public Incident? Incident { get; set; }
}
