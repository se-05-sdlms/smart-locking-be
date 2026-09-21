using smart_locking_be.Application.DTOs.Auth;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IAuthService
{
    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<AuthTokenResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<AuthTokenResponse> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task LogoutAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<UserProfileResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);

    Task RequestPasswordResetAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
