using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using Xunit;

namespace smart_locking_be.Tests.Auth;

public sealed class OtpServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static OtpService CreateService(ApplicationDbContext dbContext, bool logCode = false)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Otp:LogCode"] = logCode ? "true" : "false"
            })
            .Build();

        return new OtpService(
            dbContext,
            new Sha256TokenHashService(),
            new FixedTimeProvider(Now),
            configuration,
            NullLogger<OtpService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    #region OTP-01: OtpService.IssueAsync
    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_UTC01()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, logCode: false);

        await service.IssueAsync("0912345678", OtpPurpose.Registration, null, default);

        var challenge = await dbContext.OtpChallenges.SingleAsync();
        Assert.Equal("0912345678", challenge.DestinationPhone);
        Assert.Equal(Now.AddMinutes(10), challenge.ExpiresAt);
        Assert.Equal(0, challenge.AttemptCount);
    }

    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_UTC02()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();

        await service.IssueAsync(" 0912345678 ", OtpPurpose.PasswordReset, userId, default);

        var challenge = await dbContext.OtpChallenges.SingleAsync();
        Assert.Equal("0912345678", challenge.DestinationPhone);
        Assert.Equal(OtpPurpose.PasswordReset, challenge.Purpose);
        Assert.Equal(userId, challenge.UserId);
    }

    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_UTC03()
    {
        await using var dbContext = CreateDbContext();
        var existing = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = "hash",
            ExpiresAt = Now.AddMinutes(5),
            CreatedAt = Now.AddMinutes(-5)
        };
        dbContext.OtpChallenges.Add(existing);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        await service.IssueAsync("0912345678", OtpPurpose.Registration, null, default);

        var challenges = await dbContext.OtpChallenges.OrderBy(c => c.CreatedAt).ToListAsync();
        Assert.Equal(2, challenges.Count);
        Assert.Equal(Now, challenges[0].RevokedAt);
        Assert.Null(challenges[1].RevokedAt);
    }

    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_UTC04()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IssueAsync("091234567", OtpPurpose.Registration, null, default));
        Assert.Contains("Phone number must contain 10 digits and start with 0.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_UTC05()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IssueAsync("091234567a", OtpPurpose.Registration, null, default));
        Assert.Contains("Phone number must contain 10 digits and start with 0.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_UTC06()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, logCode: true);

        await service.IssueAsync("0912345678", OtpPurpose.Registration, null, default);

        var challenge = await dbContext.OtpChallenges.SingleAsync();
        Assert.NotNull(challenge);
    }
    #endregion

    #region OTP-02: OtpService.VerifyAndConsumeAsync
    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC01()
    {
        await using var dbContext = CreateDbContext();
        var tokenService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenService.HashToken("123456"),
            ExpiresAt = Now.AddMinutes(5),
            CreatedAt = Now.AddMinutes(-1)
        };
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        await service.VerifyAndConsumeAsync("0912345678", "123456", OtpPurpose.Registration, null, default);

        Assert.Equal(Now, challenge.UsedAt);
    }

    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC02()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAndConsumeAsync("0912345678", "123456", OtpPurpose.Registration, null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC03()
    {
        await using var dbContext = CreateDbContext();
        var tokenService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenService.HashToken("123456"),
            ExpiresAt = Now, // equals now
            CreatedAt = Now.AddMinutes(-5)
        };
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAndConsumeAsync("0912345678", "123456", OtpPurpose.Registration, null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC04()
    {
        await using var dbContext = CreateDbContext();
        var tokenService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenService.HashToken("123456"),
            ExpiresAt = Now.AddMinutes(5),
            LockedUntil = Now.AddMinutes(10), // locked
            CreatedAt = Now.AddMinutes(-1)
        };
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAndConsumeAsync("0912345678", "123456", OtpPurpose.Registration, null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC05()
    {
        await using var dbContext = CreateDbContext();
        var tokenService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenService.HashToken("123456"),
            ExpiresAt = Now.AddMinutes(5),
            AttemptCount = 1,
            CreatedAt = Now.AddMinutes(-1)
        };
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAndConsumeAsync("0912345678", "654321", OtpPurpose.Registration, null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
        Assert.Equal(2, challenge.AttemptCount);
    }

    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC06()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SystemPolicies.Add(new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            IsActive = true,
            OtpMaxAttempts = 3,
            OtpLockoutMinutes = 30
        });
        var tokenService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenService.HashToken("123456"),
            ExpiresAt = Now.AddMinutes(5),
            AttemptCount = 2,
            CreatedAt = Now.AddMinutes(-1)
        };
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAndConsumeAsync("0912345678", "654321", OtpPurpose.Registration, null, default));

        Assert.Equal(3, challenge.AttemptCount);
        Assert.Equal(Now.AddMinutes(30), challenge.LockedUntil);
    }

    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC07()
    {
        await using var dbContext = CreateDbContext();
        var tokenService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenService.HashToken("123456"),
            ExpiresAt = Now.AddMinutes(5),
            AttemptCount = 4,
            CreatedAt = Now.AddMinutes(-1)
        };
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAndConsumeAsync("0912345678", "654321", OtpPurpose.Registration, null, default));

        Assert.Equal(5, challenge.AttemptCount);
        Assert.Equal(Now.AddMinutes(15), challenge.LockedUntil);
    }

    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC08()
    {
        await using var dbContext = CreateDbContext();
        var tokenService = new Sha256TokenHashService();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenService.HashToken("123456"),
            ExpiresAt = Now.AddMinutes(5),
            UsedAt = Now.AddMinutes(-1), // already used
            CreatedAt = Now.AddMinutes(-2)
        };
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifyAndConsumeAsync("0912345678", "123456", OtpPurpose.Registration, null, default));
        Assert.Equal("Invalid OTP.", ex.Message);
    }
    #endregion
}
