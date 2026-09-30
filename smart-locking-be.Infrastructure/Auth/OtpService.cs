using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using System.Text.RegularExpressions;

namespace smart_locking_be.Infrastructure.Auth;

public sealed class OtpService(
    ApplicationDbContext dbContext,
    ITokenHashService tokenHashService,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<OtpService> logger) : IOtpService
{
    private static readonly Regex PhonePattern = new("^0[0-9]{9}$", RegexOptions.Compiled);
    private const int DefaultMaxAttempts = 5;
    private const int DefaultLockoutMinutes = 15;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task IssueAsync(
        string phoneNumber,
        OtpPurpose purpose,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        string normalizedPhone = NormalizePhone(phoneNumber);
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<OtpChallenge> activeChallenges = await dbContext.OtpChallenges
            .Where(challenge =>
                challenge.DestinationPhone == normalizedPhone &&
                challenge.Purpose == purpose &&
                challenge.UsedAt == null &&
                challenge.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (OtpChallenge challenge in activeChallenges)
        {
            challenge.RevokedAt = now;
        }

        string code = tokenHashService.CreateNumericCode();
        dbContext.OtpChallenges.Add(new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DestinationPhone = normalizedPhone,
            Purpose = purpose,
            CodeHash = tokenHashService.HashToken(code),
            ExpiresAt = now.Add(Lifetime),
            AttemptCount = 0,
            CreatedAt = now
        });

        // ponytail: opt-in local delivery; replace with the notification/SMS adapter before production.
        if (bool.TryParse(configuration["Otp:LogCode"], out bool logCode) && logCode)
        {
            logger.LogInformation("{Purpose} OTP for {PhoneNumber}: {OtpCode}", purpose, normalizedPhone, code);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task VerifyAndConsumeAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        string normalizedPhone = NormalizePhone(phoneNumber);
        DateTimeOffset now = timeProvider.GetUtcNow();
        OtpChallenge challenge = await dbContext.OtpChallenges
            .Where(otp =>
                otp.DestinationPhone == normalizedPhone &&
                otp.Purpose == purpose &&
                otp.UserId == userId &&
                otp.UsedAt == null &&
                otp.RevokedAt == null)
            .OrderByDescending(otp => otp.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw InvalidOtp();

        if (challenge.ExpiresAt <= now || challenge.LockedUntil > now)
        {
            throw InvalidOtp();
        }

        if (challenge.CodeHash != tokenHashService.HashToken(code))
        {
            (int maxAttempts, int lockoutMinutes) = await GetPolicyLimitsAsync(cancellationToken);
            challenge.AttemptCount++;
            if (challenge.AttemptCount >= maxAttempts)
            {
                challenge.LockedUntil = now.AddMinutes(lockoutMinutes);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            throw InvalidOtp();
        }

        challenge.UsedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<(int MaxAttempts, int LockoutMinutes)> GetPolicyLimitsAsync(
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.SystemPolicies
            .AsNoTracking()
            .Where(candidate => candidate.IsActive)
            .Select(candidate => new { candidate.OtpMaxAttempts, candidate.OtpLockoutMinutes })
            .SingleOrDefaultAsync(cancellationToken);

        return policy is null
            ? (DefaultMaxAttempts, DefaultLockoutMinutes)
            : (policy.OtpMaxAttempts, policy.OtpLockoutMinutes);
    }

    private static string NormalizePhone(string phoneNumber)
    {
        string normalized = phoneNumber.Trim();
        return PhonePattern.IsMatch(normalized)
            ? normalized
            : throw new ArgumentException("Phone number must contain 10 digits and start with 0.", nameof(phoneNumber));
    }

    private static InvalidOperationException InvalidOtp() => new("Invalid OTP.");
}
