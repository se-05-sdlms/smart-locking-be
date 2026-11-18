namespace smart_locking_be.Application.DTOs.Auth;

public sealed record RegisterRequest(
    string PhoneNumber,
    string OtpCode,
    string Password,
    string FullName,
    Guid RegisteredLockerId,
    string? Email,
    string? TermsVersion = null,
    string? PrivacyVersion = null,
    DateTimeOffset? ConsentedAt = null);

public sealed record RequestRegistrationOtpRequest(string PhoneNumber, string Email = "");

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

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
