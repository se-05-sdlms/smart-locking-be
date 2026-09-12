using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class ParcelAccessEvent
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid? UserId { get; set; }
    public ParcelAccessMethod Method { get; set; }
    public ParcelAccessResult Result { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public string? DeviceContext { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public Parcel Parcel { get; set; } = null!;
    public User? User { get; set; }
}
