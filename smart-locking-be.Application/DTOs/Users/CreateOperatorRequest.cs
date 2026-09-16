namespace smart_locking_be.Application.DTOs.Users;

public sealed record CreateOperatorRequest(
    string FullName,
    string Email,
    string? PhoneNumber,
    string? Password,
    Guid? BuildingId,
    Guid? LockerClusterId,
    Guid? LockerId,
    string? AssignmentReason
);
