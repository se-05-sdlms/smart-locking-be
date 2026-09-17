namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record CreateCompartmentRequest(
    string Code,
    string HardwareCode,
    int HardwareChannel
);
