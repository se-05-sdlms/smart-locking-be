using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class LockerCompartment
{
    public Guid Id { get; set; }
    public Guid LockerId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string SizeCategory { get; set; } = string.Empty;
    public LockerCompartmentOperationalStatus OperationalStatus { get; set; }
    public DoorStatus DoorStatus { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Locker Locker { get; set; } = null!;
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
    public ICollection<LockerEvent> Events { get; set; } = new List<LockerEvent>();
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
