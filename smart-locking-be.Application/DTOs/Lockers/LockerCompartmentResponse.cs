using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record LockerCompartmentResponse(
    Guid Id,
    Guid LockerId,
    string Code,
    string HardwareCode,
    int HardwareChannel,
    LockerCompartmentOperationalStatus OperationalStatus,
    DoorStatus DoorStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
