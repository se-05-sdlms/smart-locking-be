using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record UpdateUserStatusRequest(
    UserStatus Status,
    string? Reason
);
