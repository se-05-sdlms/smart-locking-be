using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class Locker
{
    public Guid Id { get; set; }
    public Guid LockerClusterId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DeviceIdentifier { get; set; } = string.Empty;
    public LockerOperationalStatus OperationalStatus { get; set; }
    public LockerConnectionStatus ConnectionStatus { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public LockerCluster LockerCluster { get; set; } = null!;
    public ICollection<LockerCompartment> Compartments { get; set; } = new List<LockerCompartment>();
    public ICollection<OperatorAssignment> OperatorAssignments { get; set; } = new List<OperatorAssignment>();
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
    public ICollection<LockerEvent> Events { get; set; } = new List<LockerEvent>();
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
