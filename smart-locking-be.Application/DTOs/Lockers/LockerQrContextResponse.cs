using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record LockerQrContextResponse(Guid LockerId, string LockerCode, string ShipperUrl, LockerOperationalStatus OperationalStatus);
