namespace smart_locking_be.Domain.Entities;

// PIN nhận hàng được cấp cho tủ; không được trả qua API danh sách công khai.
public sealed class OfflineLockerPin
{
    public Guid ParcelId { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid LockerId { get; set; }
    public string RecipientPhone { get; set; } = "";
    public string PinCode { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProvisionedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
