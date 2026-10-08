using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.DTOs.Auth;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using System.Text.RegularExpressions;

namespace smart_locking_be.Infrastructure.Auth;

public sealed class AuthService(
    ApplicationDbContext dbContext,
    IPasswordHashService passwordHashService,
    ITokenHashService tokenHashService,
    IJwtTokenService jwtTokenService,
    IOtpService otpService,
    IConfiguration configuration) : IAuthService
{
    private static readonly Regex PhonePattern = new("^0[0-9]{9}$", RegexOptions.Compiled);
    private const string InvalidCredentialsMessage = "Invalid credentials.";

    public async Task<AuthTokenResponse> RegisterAsync(
        RegisterRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        string? email = NormalizeEmail(request.Email);
        string phoneNumber = NormalizePhone(request.PhoneNumber)
            ?? throw new ArgumentException("Phone number is required.", nameof(request.PhoneNumber));

        if (!PhonePattern.IsMatch(phoneNumber))
        {
            throw new ArgumentException(
                "Phone number must contain 10 digits and start with 0.",
                nameof(request.PhoneNumber));
        }

        if (request.Password.Length < 8)
        {
            throw new ArgumentException("Password must contain at least 8 characters.", nameof(request.Password));
        }

        string fullName = request.FullName.Trim();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 150)
        {
            throw new ArgumentException("Full name must contain between 1 and 150 characters.", nameof(request.FullName));
        }

        bool exists = await dbContext.Users.AnyAsync(user =>
            (email != null && user.Email == email) || user.PhoneNumber == phoneNumber, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("User already exists.");
        }

        Locker locker = await dbContext.Lockers.SingleOrDefaultAsync(
            candidate => candidate.Id == request.RegisteredLockerId,
            cancellationToken) ?? throw new KeyNotFoundException("Registered locker not found.");
        if (locker.OperationalStatus != LockerOperationalStatus.Operational)
        {
            throw new InvalidOperationException("Registered locker is not operational.");
        }

        DeliveryApprovalMode defaultApprovalMode = DeliveryApprovalMode.Manual;

        await using IDbContextTransaction? transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await otpService.VerifyAndConsumeAsync(
            phoneNumber,
            request.OtpCode,
            OtpPurpose.Registration,
            null,
            cancellationToken);

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
        dbContext.ResidentProfiles.Add(new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RegisteredLockerId = locker.Id,
            FullName = fullName,
            DeliveryApprovalMode = defaultApprovalMode,
            FaceRecognitionEnabled = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            Action = "RegisterResident",
            EntityType = nameof(User),
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            OccurredAt = now
        });

        (AuthTokenResponse response, _) = IssueTokens(user, ipAddress, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return response;
    }

    public async Task RequestRegistrationOtpAsync(
        RequestRegistrationOtpRequest request,
        CancellationToken cancellationToken)
    {
        string phoneNumber = NormalizePhone(request.PhoneNumber)
            ?? throw new InvalidOperationException("Phone number is required.");
        bool exists = await dbContext.Users.AnyAsync(user => user.PhoneNumber == phoneNumber, cancellationToken);
        if (exists)
        {
            return;
        }

        await otpService.IssueAsync(phoneNumber, OtpPurpose.Registration, null, cancellationToken);
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

        await otpService.IssueAsync(user.PhoneNumber, OtpPurpose.PasswordReset, user.Id, cancellationToken);
    }

    public async Task ResetPasswordAsync(
        ResetPasswordRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        User user = await FindUserByIdentifierAsync(request.LoginIdentifier, cancellationToken)
            ?? throw new InvalidOperationException("Invalid OTP.");

        if (user.PhoneNumber is null)
        {
            throw new InvalidOperationException("Invalid OTP.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            throw new InvalidOperationException("Password must contain at least 8 characters.");
        }

        await otpService.VerifyAndConsumeAsync(
            user.PhoneNumber,
            request.OtpCode,
            OtpPurpose.PasswordReset,
            user.Id,
            cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        user.PasswordHash = passwordHashService.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = now;
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            Action = "ResetPassword",
            EntityType = nameof(User),
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            OccurredAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthTokenResponse> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        User user = await dbContext.Users
            .Include(candidate => candidate.RefreshTokens)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        if (!passwordHashService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect.");
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            throw new ArgumentException("Password must contain at least 8 characters.", nameof(request.NewPassword));
        if (passwordHashService.VerifyPassword(request.NewPassword, user.PasswordHash))
            throw new ArgumentException("New password must be different from the current password.", nameof(request.NewPassword));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        user.PasswordHash = passwordHashService.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = now;
        foreach (RefreshToken token in user.RefreshTokens.Where(token => token.RevokedAt == null))
        {
            token.RevokedAt = now;
            token.RevokedByIp = ipAddress;
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            Action = "Auth.PasswordChanged",
            EntityType = nameof(User),
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            IpAddress = ipAddress,
            OccurredAt = now
        });
        (AuthTokenResponse response, _) = IssueTokens(user, ipAddress, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
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
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? phoneNumber) =>
        string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
}
