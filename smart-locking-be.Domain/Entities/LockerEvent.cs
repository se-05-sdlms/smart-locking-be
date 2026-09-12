using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class LockerEvent
{
    public Guid Id { get; set; }
    public Guid LockerId { get; set; }
    public Guid? LockerCompartmentId { get; set; }
    public Guid? ActorUserId { get; set; }
    public LockerEventType EventType { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public LockerEventSeverity Severity { get; set; }
    public string? Reason { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    public Locker Locker { get; set; } = null!;
    public LockerCompartment? LockerCompartment { get; set; }
    public User? ActorUser { get; set; }
}
