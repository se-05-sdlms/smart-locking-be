using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record CompartmentPackageInfo(
    string Code,
    string Type,
    string? ResidentName,
    string? PhoneNumber,
    DateTimeOffset StoredAt,
    string Status
);

public sealed record LockerCompartmentResponse(
    Guid Id,
    Guid LockerId,
    string Code,
    string HardwareCode,
    int HardwareChannel,
    LockerCompartmentOperationalStatus OperationalStatus,
    DoorStatus DoorStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsOccupied = false,
    CompartmentPackageInfo? PackageInfo = null
);
