namespace smart_locking_be.Application.DTOs.Users;

public sealed record ResetUserCredentialsResponse(
    Guid UserId,
    string TemporaryPassword,
    bool MustChangePassword);
