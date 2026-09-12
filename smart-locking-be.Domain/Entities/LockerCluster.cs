using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class LockerCluster
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LocationDescription { get; set; }
    public LockerClusterStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Building Building { get; set; } = null!;
    public ICollection<Locker> Lockers { get; set; } = new List<Locker>();
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();
    public ICollection<OperatorAssignment> OperatorAssignments { get; set; } = new List<OperatorAssignment>();
}
