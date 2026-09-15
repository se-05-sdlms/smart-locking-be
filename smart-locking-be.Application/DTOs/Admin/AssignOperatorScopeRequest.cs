namespace smart_locking_be.Application.DTOs.Admin;

public sealed record AssignOperatorScopeRequest(
    Guid? BuildingId,
    Guid? LockerClusterId,
    Guid? LockerId,
    string? Reason
);
