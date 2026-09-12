using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class ParcelStatusHistory
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public ParcelStatus? FromStatus { get; set; }
    public ParcelStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }

    public Parcel Parcel { get; set; } = null!;
    public User? ChangedByUser { get; set; }
}
