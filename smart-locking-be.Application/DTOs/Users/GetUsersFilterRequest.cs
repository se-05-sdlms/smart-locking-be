using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record GetUsersFilterRequest(
    string? Search = null,
    UserStatus? Status = null,
    int PageNumber = 1,
    int PageSize = 10
);
