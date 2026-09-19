using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record CreateUserRequest(
    string FullName,
    string Email,
    string? PhoneNumber,
    UserRole Role,
    string? Password,
    Guid? BuildingId,
    Guid? LockerClusterId,
    Guid? LockerId,
    string? AssignmentReason
);
