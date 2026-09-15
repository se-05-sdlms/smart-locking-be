using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Admin;

public sealed record AdminOperatorListItemResponse(
    Guid UserId,
    string FullName,
    string? Email,
    string? PhoneNumber,
    UserStatus Status,
    int ActiveAssignmentsCount,
    DateTimeOffset CreatedAt
);
