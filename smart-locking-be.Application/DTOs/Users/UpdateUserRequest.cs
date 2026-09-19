namespace smart_locking_be.Application.DTOs.Users;

public sealed record UpdateUserRequest(
    string FullName,
    string? PhoneNumber,
    string? Email
);
