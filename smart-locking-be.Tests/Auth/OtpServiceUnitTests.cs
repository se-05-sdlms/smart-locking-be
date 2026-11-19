using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Auth;

public sealed class OtpServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "AUTH-OTP")]
    public async Task IssuePhone_StoresHashedChallengeAndRevokesPreviousChallenge()
    {
        await using var db = CreateDbContext();
        var previous = Challenge("0912345678", "old-hash", Now.AddMinutes(-1));
        db.OtpChallenges.Add(previous);
        await db.SaveChangesAsync();

        var sender = new FakeSmsSender();
        var service = CreateService(db, smsSender: sender);

        await service.IssueAsync(" 0912345678 ", OtpPurpose.Registration, null, default);

        var challenges = await db.OtpChallenges.OrderBy(item => item.CreatedAt).ToListAsync();
        Assert.Equal(2, challenges.Count);
        Assert.NotNull(previous.RevokedAt);
        Assert.NotEqual("123456", challenges[1].CodeHash);
        Assert.Equal("0912345678", sender.Phone);
        Assert.NotNull(sender.Code);
        Assert.Equal("Registration", sender.Purpose);
    }

    [Theory]
    [InlineData("")]
    [InlineData("912345678")]
    [InlineData("091234567a")]
    [Trait("Category", "AUTH-OTP")]
    public async Task IssuePhone_RejectsInvalidPhone(string phone)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, smsSender: new FakeSmsSender());

        await Assert.ThrowsAsync<BadRequestException>(() => service.IssueAsync(
            phone, OtpPurpose.Registration, null, default));
    }

    [Fact]
    [Trait("Category", "AUTH-OTP")]
    public async Task IssuePhone_RequiresConfiguredSenderWhenHosted()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, hostEnvironment: new FakeHostEnvironment());

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => service.IssueAsync(
            "0912345678", OtpPurpose.Registration, null, default));
        Assert.Empty(db.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-OTP")]
    public async Task VerifyPhone_ConsumesCorrectCode()
    {
        await using var db = CreateDbContext();
        var hash = new Sha256TokenHashService();
        var challenge = Challenge("0912345678", hash.HashToken("123456"), Now.AddMinutes(-1));
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();

        await CreateService(db).VerifyAndConsumeAsync(
            "0912345678", "123456", OtpPurpose.Registration, null, default);

        Assert.NotNull(challenge.UsedAt);
    }

    [Fact]
    [Trait("Category", "AUTH-OTP")]
    public async Task VerifyPhone_IncrementsAttemptsAndLocksAfterPolicyLimit()
    {
        await using var db = CreateDbContext();
        var hash = new Sha256TokenHashService();
        var challenge = Challenge("0912345678", hash.HashToken("123456"), Now.AddMinutes(-1));
        db.OtpChallenges.Add(challenge);
        db.SystemPolicies.Add(new SystemPolicy
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            EffectiveFrom = Now.AddHours(-1),
            OtpMaxAttempts = 2,
            OtpLockoutMinutes = 20
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<BadRequestException>(() => service.VerifyAndConsumeAsync(
            "0912345678", "000000", OtpPurpose.Registration, null, default));
        await Assert.ThrowsAsync<BadRequestException>(() => service.VerifyAndConsumeAsync(
            "0912345678", "000000", OtpPurpose.Registration, null, default));

        Assert.Equal(2, challenge.AttemptCount);
        Assert.True(challenge.LockedUntil > Now);
        Assert.Contains(db.AuditLogs, item => item.Action == "Auth.OtpLocked");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [Trait("Category", "AUTH-OTP")]
    public async Task VerifyPhone_RejectsExpiredOrUsedChallenge(bool expired, bool used)
    {
        await using var db = CreateDbContext();
        var hash = new Sha256TokenHashService();
        var challenge = Challenge("0912345678", hash.HashToken("123456"),
            expired ? Now.AddMinutes(-11) : Now.AddMinutes(-1));
        if (used) challenge.UsedAt = Now.AddMinutes(-1);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db).VerifyAndConsumeAsync(
            "0912345678", "123456", OtpPurpose.Registration, null, default));
    }

    [Fact]
    [Trait("Category", "AUTH-OTP")]
    public async Task IssueEmail_NormalizesDestinationAndSendsCodeAfterPersistence()
    {
        await using var db = CreateDbContext();
        var sender = new FakeEmailSender();
        await CreateService(db, emailSender: sender).IssueEmailAsync(
            " Resident@Example.COM ", OtpPurpose.PasswordReset, null, default);

        var challenge = Assert.Single(db.OtpChallenges);
        Assert.Equal("resident@example.com", challenge.DestinationEmail);
        Assert.Equal("resident@example.com", sender.To);
        Assert.Contains("password reset", sender.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(sender.Body);
    }

    [Fact]
    [Trait("Category", "AUTH-OTP")]
    public async Task IssueEmail_RequiresEmailSender()
    {
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => CreateService(db).IssueEmailAsync(
            "resident@example.com", OtpPurpose.PasswordReset, null, default));
        Assert.Empty(db.OtpChallenges);
    }

    [Fact]
    [Trait("Category", "AUTH-OTP")]
    public async Task VerifyEmail_ConsumesCorrectCodeAndNormalizesDestination()
    {
        await using var db = CreateDbContext();
        var hash = new Sha256TokenHashService();
        var challenge = ChallengeEmail("resident@example.com", hash.HashToken("654321"));
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();

        await CreateService(db).VerifyAndConsumeEmailAsync(
            " Resident@Example.COM ", "654321", OtpPurpose.PasswordReset, null, default);

        Assert.NotNull(challenge.UsedAt);
    }

    private static OtpService CreateService(
        ApplicationDbContext db,
        IEmailSender? emailSender = null,
        ISmsSender? smsSender = null,
        IHostEnvironment? hostEnvironment = null) => new(
        db,
        new Sha256TokenHashService(),
        new FixedTimeProvider(Now),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
        NullLogger<OtpService>.Instance,
        emailSender,
        smsSender,
        hostEnvironment);

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static OtpChallenge Challenge(string phone, string hash, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        DestinationPhone = phone,
        Purpose = OtpPurpose.Registration,
        CodeHash = hash,
        CreatedAt = createdAt,
        ExpiresAt = createdAt.AddMinutes(10)
    };

    private static OtpChallenge ChallengeEmail(string email, string hash) => new()
    {
        Id = Guid.NewGuid(),
        DestinationPhone = string.Empty,
        DestinationEmail = email,
        Purpose = OtpPurpose.PasswordReset,
        CodeHash = hash,
        CreatedAt = Now.AddMinutes(-1),
        ExpiresAt = Now.AddMinutes(9)
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSmsSender : ISmsSender
    {
        public string? Phone { get; private set; }
        public string? Code { get; private set; }
        public string? Purpose { get; private set; }

        public Task SendOtpAsync(string destinationPhone, string code, string purpose, Guid messageId,
            CancellationToken cancellationToken)
        {
            Phone = destinationPhone;
            Code = code;
            Purpose = purpose;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public string? To { get; private set; }
        public string? Subject { get; private set; }
        public string? Body { get; private set; }

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            To = to;
            Subject = subject;
            Body = body;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
