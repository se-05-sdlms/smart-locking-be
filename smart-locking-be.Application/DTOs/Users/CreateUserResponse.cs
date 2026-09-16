using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record CreateUserResponse(
    Guid Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    UserRole Role,
    UserStatus Status,
    string TemporaryPassword,
    DateTimeOffset CreatedAt
);
