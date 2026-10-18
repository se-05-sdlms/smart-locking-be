using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Auth;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicyNames.Auth)]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpGet("registration-lockers")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRegistrationLockers(CancellationToken cancellationToken) =>
        Ok(await authService.GetRegistrationLockersAsync(cancellationToken));

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.RegisterAsync(request, GetIpAddress(), cancellationToken));

    [HttpPost("registration-otp/request")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestRegistrationOtp(
        RequestRegistrationOtpRequest request,
        CancellationToken cancellationToken)
    {
        await authService.RequestRegistrationOtpAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.LoginAsync(request, GetIpAddress(), cancellationToken));

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.RefreshTokenAsync(request, GetIpAddress(), cancellationToken));

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request, GetIpAddress(), cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        Guid parsedUserId = User.GetRequiredUserId();
        return Ok(await authService.GetCurrentUserAsync(parsedUserId, cancellationToken));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await authService.ResetPasswordAsync(request, GetIpAddress(), cancellationToken);
        return NoContent();
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();
        return Ok(await authService.ChangePasswordAsync(
            userId, request, GetIpAddress(), cancellationToken));
    }
}
