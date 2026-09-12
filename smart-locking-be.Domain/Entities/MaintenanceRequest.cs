using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class MaintenanceRequest
{
    public Guid Id { get; set; }
    public Guid LockerId { get; set; }
    public Guid? LockerCompartmentId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? IncidentId { get; set; }
    public MaintenancePriority Priority { get; set; }
    public MaintenanceStatus Status { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ResolutionSummary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public Locker Locker { get; set; } = null!;
    public LockerCompartment? LockerCompartment { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public Incident? Incident { get; set; }
    public ICollection<MaintenanceActivity> Activities { get; set; } = new List<MaintenanceActivity>();
}
