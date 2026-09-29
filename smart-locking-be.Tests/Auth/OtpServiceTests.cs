using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Auth;

public sealed class OtpServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueAsync_RevokesPreviousChallengeAndStoresOnlyHash()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        OtpChallenge previous = CreateChallenge("123456");
        dbContext.OtpChallenges.Add(previous);
        await dbContext.SaveChangesAsync();

        OtpService service = CreateService(dbContext);
        await service.IssueAsync("0912345678", OtpPurpose.Registration, null, default);

        List<OtpChallenge> challenges = await dbContext.OtpChallenges.OrderBy(item => item.CreatedAt).ToListAsync();
        Assert.Equal(2, challenges.Count);
        Assert.Equal(Now, challenges[0].RevokedAt);
        Assert.NotEqual("123456", challenges[1].CodeHash);
        Assert.Equal(64, challenges[1].CodeHash.Length);
    }

    [Fact]
    public async Task VerifyAndConsumeAsync_WithValidCode_MarksChallengeUsed()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        OtpChallenge challenge = CreateChallenge("123456");
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).VerifyAndConsumeAsync(
            "0912345678", "123456", OtpPurpose.Registration, null, default);

        Assert.Equal(Now, challenge.UsedAt);
    }

    [Fact]
    public async Task VerifyAndConsumeAsync_AfterFiveWrongCodes_LocksChallenge()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        OtpChallenge challenge = CreateChallenge("123456");
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();
        OtpService service = CreateService(dbContext);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAndConsumeAsync(
                "0912345678", "654321", OtpPurpose.Registration, null, default));
        }

        Assert.Equal(5, challenge.AttemptCount);
        Assert.Equal(Now.AddMinutes(15), challenge.LockedUntil);
    }

    [Theory]
    [InlineData(true, false, OtpPurpose.Registration)]
    [InlineData(false, true, OtpPurpose.Registration)]
    [InlineData(false, false, OtpPurpose.PasswordReset)]
    public async Task VerifyAndConsumeAsync_WithUnavailableChallenge_Rejects(
        bool expired,
        bool used,
        OtpPurpose requestedPurpose)
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        OtpChallenge challenge = CreateChallenge("123456");
        if (expired)
        {
            challenge.ExpiresAt = Now;
        }
        if (used)
        {
            challenge.UsedAt = Now.AddMinutes(-1);
        }
        dbContext.OtpChallenges.Add(challenge);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(dbContext).VerifyAndConsumeAsync(
            "0912345678", "123456", requestedPurpose, null, default));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static OtpService CreateService(ApplicationDbContext dbContext) =>
        new(
            dbContext,
            new Sha256TokenHashService(),
            new FixedTimeProvider(Now),
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<OtpService>.Instance);

    private static OtpChallenge CreateChallenge(string code)
    {
        var tokenHashService = new Sha256TokenHashService();
        return new OtpChallenge
        {
            Id = Guid.NewGuid(),
            DestinationPhone = "0912345678",
            Purpose = OtpPurpose.Registration,
            CodeHash = tokenHashService.HashToken(code),
            ExpiresAt = Now.AddMinutes(10),
            CreatedAt = Now.AddMinutes(-1)
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

}
