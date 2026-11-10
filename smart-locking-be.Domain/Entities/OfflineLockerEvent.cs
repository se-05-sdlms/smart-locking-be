namespace smart_locking_be.Domain.Entities;

// Nhật ký thiết bị đã nhận được lưu bền vững; cùng Event ID từ điện thoại/MQTT chỉ lưu một lần.
public sealed class OfflineLockerEvent
{
    public Guid Id { get; set; }
    public long Sequence { get; set; }
    public string? PinCode { get; set; }
    public Guid DeliveryId { get; set; }
    public string DeviceIdentifier { get; set; } = "";
    public int HardwareChannel { get; set; }
    public string Kind { get; set; } = "";
    public string RecipientPhone { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long ElapsedMs { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ReviewReason { get; set; }
}
