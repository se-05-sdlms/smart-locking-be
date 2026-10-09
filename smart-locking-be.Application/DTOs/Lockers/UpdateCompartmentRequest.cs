namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record UpdateCompartmentRequest(string Code, string HardwareCode, int HardwareChannel);

public sealed record DeactivateLockerResourceRequest(string Reason);
