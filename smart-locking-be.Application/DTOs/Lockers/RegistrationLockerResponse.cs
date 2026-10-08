namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record RegistrationLockerResponse(
    Guid Id,
    string Code,
    string Address);
