using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Auth;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Auth;

public sealed class AuthService(
    ApplicationDbContext dbContext,
    IPasswordHashService passwordHashService,
    ITokenHashService tokenHashService,
    IJwtTokenService jwtTokenService,
    IConfiguration configuration,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid credentials.";
    private const int OtpMaxAttempts = 5;
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OtpLockout = TimeSpan.FromMinutes(15);

    public async Task<AuthTokenResponse> RegisterAsync(
        RegisterRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        string? email = NormalizeEmail(request.Email);
        string? phoneNumber = NormalizePhone(request.PhoneNumber);

        if (email is null && phoneNumber is null)
        {
            throw new InvalidOperationException("Email or phone number is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Password is required.");
        }

        bool exists = await dbContext.Users.AnyAsync(user =>
            (email != null && user.Email == email) ||
            (phoneNumber != null && user.PhoneNumber == phoneNumber), cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("User already exists.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PhoneNumber = phoneNumber,
            PasswordHash = passwordHashService.HashPassword(request.Password),
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Users.Add(user);

        (AuthTokenResponse response, _) = IssueTokens(user, ipAddress, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<AuthTokenResponse> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        User user = await FindUserByIdentifierAsync(request.LoginIdentifier, cancellationToken)
            ?? throw new InvalidOperationException(InvalidCredentialsMessage);

        if (user.Status != UserStatus.Active ||
            !passwordHashService.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new InvalidOperationException(InvalidCredentialsMessage);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        user.LastLoginAt = now;
        user.UpdatedAt = now;

        (AuthTokenResponse response, _) = IssueTokens(user, ipAddress, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<AuthTokenResponse> RefreshTokenAsync(
        RefreshTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string tokenHash = tokenHashService.HashToken(request.RefreshToken);
        RefreshToken token = await dbContext.RefreshTokens
            .Include(refreshToken => refreshToken.User)
            .SingleOrDefaultAsync(refreshToken =>
                refreshToken.TokenHash == tokenHash &&
                refreshToken.RevokedAt == null &&
                refreshToken.ExpiresAt > now, cancellationToken)
            ?? throw new InvalidOperationException("Invalid refresh token.");

        if (token.User.Status != UserStatus.Active)
        {
            throw new InvalidOperationException("Invalid refresh token.");
        }

        (AuthTokenResponse response, RefreshToken replacementToken) = IssueTokens(token.User, ipAddress, now);
        token.RevokedAt = now;
        token.RevokedByIp = ipAddress;
        token.ReplacedByTokenId = replacementToken.Id;

        await dbContext.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task LogoutAsync(
        RefreshTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        string tokenHash = tokenHashService.HashToken(request.RefreshToken);
        RefreshToken? token = await dbContext.RefreshTokens.SingleOrDefaultAsync(refreshToken =>
            refreshToken.TokenHash == tokenHash &&
            refreshToken.RevokedAt == null, cancellationToken);

        if (token is null)
        {
            return;
        }

        token.RevokedAt = DateTimeOffset.UtcNow;
        token.RevokedByIp = ipAddress;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserProfileResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        User user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        return ToProfile(user);
    }

    public async Task RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        User? user = await FindUserByIdentifierAsync(request.LoginIdentifier, cancellationToken);
        if (user?.PhoneNumber is null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string code = tokenHashService.CreateNumericCode();

        dbContext.OtpChallenges.Add(new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DestinationPhone = user.PhoneNumber,
            Purpose = OtpPurpose.PasswordReset,
            CodeHash = tokenHashService.HashToken(code),
            ExpiresAt = now.Add(OtpLifetime),
            AttemptCount = 0,
            CreatedAt = now
        });

        // ponytail: first pass has no SMS provider; replace this with a notification service before production.
        logger.LogInformation("Password reset OTP for user {UserId}: {OtpCode}", user.Id, code);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        User user = await FindUserByIdentifierAsync(request.LoginIdentifier, cancellationToken)
            ?? throw new InvalidOperationException("Invalid OTP.");

        if (user.PhoneNumber is null)
        {
            throw new InvalidOperationException("Invalid OTP.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        OtpChallenge challenge = await dbContext.OtpChallenges
            .Where(otp =>
                otp.UserId == user.Id &&
                otp.DestinationPhone == user.PhoneNumber &&
                otp.Purpose == OtpPurpose.PasswordReset &&
                otp.UsedAt == null &&
                otp.RevokedAt == null)
            .OrderByDescending(otp => otp.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Invalid OTP.");

        if (challenge.ExpiresAt <= now || challenge.LockedUntil > now)
        {
            throw new InvalidOperationException("Invalid OTP.");
        }

        if (challenge.CodeHash != tokenHashService.HashToken(request.OtpCode))
        {
            challenge.AttemptCount++;
            if (challenge.AttemptCount >= OtpMaxAttempts)
            {
                challenge.LockedUntil = now.Add(OtpLockout);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Invalid OTP.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new InvalidOperationException("Password is required.");
        }

        user.PasswordHash = passwordHashService.HashPassword(request.NewPassword);
        user.UpdatedAt = now;
        challenge.UsedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private (AuthTokenResponse Response, RefreshToken RefreshToken) IssueTokens(User user, string? ipAddress, DateTimeOffset now)
    {
        (string accessToken, DateTimeOffset accessTokenExpiresAt) = jwtTokenService.CreateAccessToken(user);
        string refreshToken = tokenHashService.CreateSecureToken();
        RefreshToken storedRefreshToken = new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHashService.HashToken(refreshToken),
            ExpiresAt = now.AddDays(GetRefreshTokenDays()),
            CreatedAt = now,
            CreatedByIp = ipAddress
        };

        dbContext.RefreshTokens.Add(storedRefreshToken);

        AuthTokenResponse response = new(
            accessToken,
            refreshToken,
            accessTokenExpiresAt,
            storedRefreshToken.ExpiresAt,
            ToProfile(user));

        return (response, storedRefreshToken);
    }

    private async Task<User?> FindUserByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        string normalized = identifier.Trim();
        string normalizedEmail = NormalizeEmail(normalized) ?? normalized;

        return await dbContext.Users.SingleOrDefaultAsync(user =>
            user.Email == normalizedEmail ||
            user.PhoneNumber == normalized, cancellationToken);
    }

    private int GetRefreshTokenDays() =>
        int.TryParse(configuration["Jwt:RefreshTokenDays"], out int days) ? days : 30;

    private static UserProfileResponse ToProfile(User user) => new(
        user.Id,
        user.PhoneNumber,
        user.Email,
        user.Role.ToString(),
        user.Status.ToString(),
        user.MustChangePassword,
        user.LastLoginAt);

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();

    private static string? NormalizePhone(string? phoneNumber) =>
        string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
}
