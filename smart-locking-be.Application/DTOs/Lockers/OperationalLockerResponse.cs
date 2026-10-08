using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record OperationalLockerResponse(
    Guid Id,
    string Code,
    string Address,
    LockerOperationalStatus Status,
    LockerConnectionStatus Connection,
    DateTimeOffset? LastSeenAt,
    int AvailableCompartments,
    int StoredParcels,
    int OpenIncidents);
