using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

/// <summary>Thông tin giao dịch cư dân gửi đồ dùng trong luồng Bluetooth.</summary>
public sealed record BluetoothReturnContext(
    Guid LockerId,
    Guid? CompartmentId,
    ReturnRequestStatus Status,
    DateTimeOffset? ExpiresAt);
