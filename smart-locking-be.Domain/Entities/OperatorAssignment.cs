namespace smart_locking_be.Domain.Entities;

public sealed class OperatorAssignment
{
    public Guid Id { get; set; }
    public Guid OperatorUserId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? LockerClusterId { get; set; }
    public Guid? LockerId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? Reason { get; set; }

    public User OperatorUser { get; set; } = null!;
    public Building? Building { get; set; }
    public LockerCluster? LockerCluster { get; set; }
    public Locker? Locker { get; set; }
    public User AssignedByUser { get; set; } = null!;
}
