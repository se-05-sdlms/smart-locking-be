using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

/// <summary>Thông tin giao hàng cho cư dân dùng trong luồng Bluetooth.</summary>
public sealed record BluetoothDeliveryContext(
    Guid LockerId,
    Guid? CompartmentId,
    DeliveryRequestStatus Status,
    DateTimeOffset? ExpiresAt);
