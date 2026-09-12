using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class Incident
{
    public Guid Id { get; set; }
    public Guid? ReporterUserId { get; set; }
    public string? ReporterName { get; set; }
    public string? ReporterPhone { get; set; }
    public Guid? DeliveryRequestId { get; set; }
    public Guid? ParcelId { get; set; }
    public Guid? LockerId { get; set; }
    public Guid? LockerCompartmentId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public Guid? AssignedOperatorUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public IncidentSource Source { get; set; }
    public IncidentStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ResolutionSummary { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? ReporterUser { get; set; }
    public DeliveryRequest? DeliveryRequest { get; set; }
    public Parcel? Parcel { get; set; }
    public Locker? Locker { get; set; }
    public LockerCompartment? LockerCompartment { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }
    public User? AssignedOperatorUser { get; set; }
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<IncidentAction> Actions { get; set; } = new List<IncidentAction>();
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
