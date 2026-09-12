using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class Parcel
{
    public Guid Id { get; set; }
    public Guid DeliveryRequestId { get; set; }
    public string ParcelCode { get; set; } = string.Empty;
    public ParcelStatus Status { get; set; }
    public DateTimeOffset StoredAt { get; set; }
    public DateTimeOffset PickupDueAt { get; set; }
    public DateTimeOffset MaxStorageUntil { get; set; }
    public DateTimeOffset? RetrievedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public string? RemovalReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public DeliveryRequest DeliveryRequest { get; set; } = null!;
    public User? RemovedByUser { get; set; }
    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();
    public ICollection<ParcelStatusHistory> StatusHistory { get; set; } = new List<ParcelStatusHistory>();
    public ICollection<ParcelAccessEvent> AccessEvents { get; set; } = new List<ParcelAccessEvent>();
    public OverdueCharge? OverdueCharge { get; set; }
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
