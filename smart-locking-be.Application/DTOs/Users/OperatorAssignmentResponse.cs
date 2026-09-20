namespace smart_locking_be.Application.DTOs.Users;

public sealed record OperatorAssignmentResponse(
    Guid Id,
    Guid LockerId,
    string? LockerCode,
    DateTimeOffset AssignedAt,
    DateTimeOffset? RevokedAt,
    string? Reason
);
