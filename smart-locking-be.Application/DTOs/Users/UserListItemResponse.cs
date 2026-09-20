using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record UserListItemResponse(
    Guid Id,
    string FullName,
    string? PhoneNumber,
    string? Email,
    UserRole Role,
    UserStatus Status,
    int ActiveAssignmentsCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt
);
