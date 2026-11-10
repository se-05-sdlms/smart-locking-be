namespace smart_locking_be.Application.Errors;

public static class AuthErrorCodes
{
    public const string InvalidCredentials = "auth/invalid_credentials";
    public const string InvalidToken = "auth/invalid_token";
    public const string AuthenticationRequired = "auth/authentication_required";
    public const string TokenExpired = "auth/token_expired";
    public const string InvalidRefreshToken = "auth/invalid_refresh_token";
    public const string InvalidOtp = "auth/invalid_otp";
    public const string UserAlreadyExists = "auth/user_already_exists";
    public const string PasswordChangeRequired = "auth/password_change_required";
    public const string InvalidPassword = "auth/invalid_password";
    public const string MissingGuestSession = "auth/missing_guest_session";
    public const string InvalidGuestSession = "auth/invalid_guest_session";
}
