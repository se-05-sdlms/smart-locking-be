namespace smart_locking_be.Application.DTOs.Auth;

public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    UserProfileResponse User);

public sealed record UserProfileResponse(
    Guid Id,
    string? PhoneNumber,
    string? Email,
    string Role,
    string Status,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt);
