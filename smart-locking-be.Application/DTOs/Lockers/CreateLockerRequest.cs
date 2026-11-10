namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record CreateLockerRequest(
    string Code,
    string Address,
    string RecoveryAddress,
    string DeviceIdentifier
);
