using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record CreateOperatorResponse(
    Guid UserId,
    string FullName,
    string Email,
    string? PhoneNumber,
    string TemporaryPassword,
    UserStatus Status,
    DateTimeOffset CreatedAt
);
