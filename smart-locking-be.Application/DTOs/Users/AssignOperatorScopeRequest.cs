namespace smart_locking_be.Application.DTOs.Users;

public sealed record AssignOperatorScopeRequest(
    Guid LockerId,
    string? Reason
);
