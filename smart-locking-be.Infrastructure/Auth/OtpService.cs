using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
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
    ILogger<OtpService> logger,
    IEmailSender? emailSender = null,
    ISmsSender? smsSender = null,
    IHostEnvironment? hostEnvironment = null) : IOtpService
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

        string code = tokenHashService.CreateNumericCode();
        OtpChallenge issued = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DestinationPhone = normalizedPhone,
            Purpose = purpose,
            CodeHash = tokenHashService.HashToken(code),
            ExpiresAt = now.Add(Lifetime),
            AttemptCount = 0,
            CreatedAt = now
        };

        // Hosted API always injects a sender. Keep direct challenge-store construction compatible.
        // Never acknowledge a hosted request when its transport was omitted from DI.
        if (smsSender is null && hostEnvironment is not null)
            throw new ServiceUnavailableException("sms/unavailable", "SMS delivery is not configured.");
        if (smsSender is not null)
            await smsSender.SendOtpAsync(normalizedPhone, code, purpose.ToString(), issued.Id, cancellationToken);
        foreach (OtpChallenge challenge in activeChallenges) challenge.RevokedAt = now;
        dbContext.OtpChallenges.Add(issued);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Created {Purpose} OTP challenge {ChallengeId}; sandbox code logging requested: {LogCode}",
            purpose, issued.Id, configuration.GetValue<bool>("Otp:LogCode"));
    }

    public async Task VerifyAndConsumeAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        string normalizedPhone = NormalizePhone(phoneNumber);
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

        await VerifyChallengeAsync(challenge, code, cancellationToken);
    }

    public async Task IssueEmailAsync(string email, OtpPurpose purpose, Guid? userId, CancellationToken cancellationToken)
    {
        if (emailSender is null)
            throw new ServiceUnavailableException(UserErrorCodes.EmailUnavailable, "Email delivery is unavailable.");
        string destination = email.Trim().ToLowerInvariant();
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<OtpChallenge> active = await dbContext.OtpChallenges
            .Where(item => item.DestinationEmail == destination && item.Purpose == purpose &&
                item.UsedAt == null && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (OtpChallenge item in active) item.RevokedAt = now;

        string code = tokenHashService.CreateNumericCode();
        dbContext.OtpChallenges.Add(new OtpChallenge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DestinationPhone = string.Empty,
            DestinationEmail = destination,
            Purpose = purpose,
            CodeHash = tokenHashService.HashToken(code),
            ExpiresAt = now.Add(Lifetime),
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        string subject = purpose == OtpPurpose.Registration
            ? "Smart Locking registration code"
            : "Smart Locking password reset code";
        await emailSender.SendAsync(destination, subject,
            $"Your verification code is {code}. It expires in 10 minutes.", cancellationToken);
    }

    public async Task VerifyAndConsumeEmailAsync(string email, string code, OtpPurpose purpose, Guid? userId,
        CancellationToken cancellationToken)
    {
        string destination = email.Trim().ToLowerInvariant();
        OtpChallenge challenge = await dbContext.OtpChallenges
            .Where(item => item.DestinationEmail == destination &&
                item.Purpose == purpose && item.UserId == userId &&
                item.UsedAt == null && item.RevokedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw InvalidOtp();

        await VerifyChallengeAsync(challenge, code, cancellationToken);
    }

    private async Task VerifyChallengeAsync(OtpChallenge challenge, string code, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

        if (dbContext.Database.IsRelational())
        {
            // Serialize both failures and consumption, including callers that have already
            // started a transaction. Reload after waiting: the tracked value may be stale.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"OtpChallenge\" WHERE \"Id\" = {challenge.Id} FOR UPDATE", cancellationToken);
            await dbContext.Entry(challenge).ReloadAsync(cancellationToken);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (dbContext.Entry(challenge).State == EntityState.Detached || challenge.UsedAt is not null ||
            challenge.RevokedAt is not null || challenge.ExpiresAt <= now || challenge.LockedUntil > now)
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
                dbContext.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = challenge.UserId,
                    Action = "Auth.OtpLocked",
                    EntityType = nameof(OtpChallenge),
                    EntityId = challenge.Id,
                    Result = AuditLogResult.Failed,
                    Details = $"Purpose={challenge.Purpose}",
                    OccurredAt = now
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw InvalidOtp();
        }

        challenge.UsedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
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
            : throw BadRequestException.For("phoneNumber", "Phone number must contain 10 digits and start with 0.");
    }

    private static BadRequestException InvalidOtp() => new(AuthErrorCodes.InvalidOtp, "Invalid OTP.");
}
