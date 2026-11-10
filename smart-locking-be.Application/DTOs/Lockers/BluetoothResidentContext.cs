using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

/// <summary>Thông tin cư dân và tủ dùng để kiểm tra điều kiện thao tác Bluetooth.</summary>
public sealed record BluetoothResidentContext(
    Guid ProfileId,
    Guid LockerId,
    string Device,
    string? Phone,
    UserStatus AccountStatus,
    UserRole Role,
    LockerOperationalStatus? LockerStatus);
