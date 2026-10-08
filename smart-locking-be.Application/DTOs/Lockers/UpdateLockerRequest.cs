namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record UpdateLockerRequest(
    string Code,
    string Address,
    string RecoveryAddress,
    string DeviceIdentifier
);
