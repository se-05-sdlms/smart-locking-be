using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? ParcelId { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public NotificationDeliveryStatus DeliveryStatus { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public Parcel? Parcel { get; set; }
    public Incident? Incident { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }
}
