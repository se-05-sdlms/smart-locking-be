namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record PublicLockerResponse(string Code, string Address, bool IsOperational);
