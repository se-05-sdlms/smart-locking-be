namespace smart_locking_be.Application.DTOs.Users;

public sealed record OperatorAssignmentResponse(
    Guid Id,
    Guid? BuildingId,
    string? BuildingName,
    Guid? LockerClusterId,
    string? LockerClusterName,
    Guid? LockerId,
    string? LockerCode,
    DateTimeOffset AssignedAt,
    DateTimeOffset? RevokedAt,
    string? Reason
);
