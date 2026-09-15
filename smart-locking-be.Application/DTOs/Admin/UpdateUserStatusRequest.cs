using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Admin;

public sealed record UpdateUserStatusRequest(
    UserStatus NewStatus,
    string? Reason
);
