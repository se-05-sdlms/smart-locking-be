using System.ComponentModel.DataAnnotations;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record OfflinePinSyncRequest([Required, MinLength(1), MaxLength(100)] List<string> Events);
// Tra cứu các event đã được adapter thiết bị lưu; client không gửi proof để áp dụng nghiệp vụ.
public sealed record OfflinePinReconcileRequest([Required, MinLength(1), MaxLength(100)] List<Guid> EventIds);
public sealed record OfflinePinSyncResult(Guid EventId, string Status, string? ReviewReason, string Acknowledgment);
public sealed record OfflinePinSyncResponse(IReadOnlyList<OfflinePinSyncResult> Acknowledged);

public sealed record OfflinePinEventResponse(Guid EventId, Guid DeliveryId, string DeviceIdentifier, int HardwareChannel,
    string Kind, long Sequence, string RecipientPhone, DateTimeOffset? OpenedAt, DateTimeOffset? ClosedAt,
    DateTimeOffset ReceivedAt, string Status, string? ReviewReason);

// Dữ liệu sự kiện sau khi adapter đã đọc và xác nhận thông điệp.
public sealed record OfflinePinEvidence(Guid EventId, Guid DeliveryId, string DeviceIdentifier,
    int Channel, string Kind, string Phone, string Pin, long OpenedUnix, long ClosedUnix, long ElapsedMs, long Sequence);
