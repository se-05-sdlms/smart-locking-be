namespace smart_locking_be.Application.DTOs.Users;

public sealed record AssignOperatorScopeRequest(
    Guid? BuildingId,
    Guid? LockerClusterId,
    Guid? LockerId,
    string? Reason
);
