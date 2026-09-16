using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record OperatorDetailResponse(
    Guid UserId,
    string FullName,
    string? Email,
    string? PhoneNumber,
    UserStatus Status,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OperatorAssignmentResponse> Assignments
);
