using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record LockerSummaryResponse(
    Guid Id,
    string Code,
    string Address,
    string RecoveryAddress,
    string DeviceIdentifier,
    LockerOperationalStatus OperationalStatus,
    LockerConnectionStatus ConnectionStatus,
    DateTimeOffset? LastSeenAt,
    int TotalCompartments,
    int ActiveCompartments,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
