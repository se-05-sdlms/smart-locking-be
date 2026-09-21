namespace smart_locking_be.Application.DTOs.Auth;

public sealed record RegisterRequest(
    string? PhoneNumber,
    string? Email,
    string Password);

public sealed record LoginRequest(
    string LoginIdentifier,
    string Password);

public sealed record RefreshTokenRequest(
    string RefreshToken);

public sealed record RequestPasswordResetRequest(
    string LoginIdentifier);

public sealed record ResetPasswordRequest(
    string LoginIdentifier,
    string OtpCode,
    string NewPassword);
