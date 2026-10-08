namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record UpdateOperationalStatusRequest(string Status, string Reason);
