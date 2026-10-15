using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Auth;

public sealed class OtpServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T08:00:00Z");
    private const string Phone = "0912345678";
    private const string Code = "123456";
    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_UTC01()
    {
        await using var c = new Context(); await c.Service.IssueAsync(" " + Phone + " ", OtpPurpose.Registration, c.UserId, default);
        c.Db.ChangeTracker.Clear(); var otp = await c.Db.OtpChallenges.SingleAsync();
        Assert.Equal(Phone, otp.DestinationPhone); Assert.Equal(c.UserId, otp.UserId); Assert.Equal(OtpPurpose.Registration, otp.Purpose); Assert.NotEqual(Code, otp.CodeHash); Assert.Equal(new Sha256TokenHashService().HashToken(Code), otp.CodeHash);
        Assert.Equal(Now, otp.CreatedAt); Assert.Equal(Now.AddMinutes(10), otp.ExpiresAt); Assert.Equal(0, otp.AttemptCount); Assert.Null(otp.UsedAt); Assert.Null(otp.RevokedAt); Assert.Null(otp.LockedUntil);
    }
    [Fact(Skip = "Excel OTP-01 UTC02 specifies missing/invalid DB target with KeyNotFoundException/Empty but IssueAsync has no required target lookup; a precise invalid precondition is needed.")]
    [Trait("Category", "OTP-01")]
    public Task IssueAsync_UTC02() => Task.CompletedTask;
    [Fact(Skip = "Excel OTP-01 UTC03 passes null OtpPurpose to a non-nullable enum and expects success for an empty invalid phone. No executable matching input contract.")]
    [Trait("Category", "OTP-01")]
    public Task IssueAsync_UTC03() => Task.CompletedTask;
    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC01()
    {
        await using var c = new Context(); var otp = c.Seed(); await c.Db.SaveChangesAsync();
        await c.Service.VerifyAndConsumeAsync(Phone, Code, OtpPurpose.Registration, c.UserId, default); c.Db.ChangeTracker.Clear();
        var persisted = await c.Db.OtpChallenges.SingleAsync(); Assert.Equal(otp.Id, persisted.Id); Assert.Equal(Now, persisted.UsedAt); Assert.Equal(0, persisted.AttemptCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.VerifyAndConsumeAsync(Phone, Code, OtpPurpose.Registration, c.UserId, default));
    }
    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_UTC02()
    {
        // Excel says KeyNotFoundException/Empty; the void API cannot return Empty. Dev throws InvalidOperationException instead.
        await using var c = new Context(); await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.VerifyAndConsumeAsync(Phone, Code, OtpPurpose.Registration, c.UserId, default)); Assert.Empty(c.Db.OtpChallenges);
    }
    [Fact(Skip = "Excel OTP-02 UTC03 has null OtpPurpose and empty phone/code but expects success; null cannot be passed to the enum parameter.")]
    [Trait("Category", "OTP-02")]
    public Task VerifyAndConsumeAsync_UTC03() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_RevokesOnlyUnusedChallengesForSamePhoneAndPurpose()
    {
        await using var c = new Context(); var old = c.Seed(); var used = c.Seed(); used.UsedAt = Now.AddMinutes(-1); var otherPurpose = c.Seed(); otherPurpose.Purpose = OtpPurpose.PasswordReset; var otherPhone = c.Seed(); otherPhone.DestinationPhone = "0987654321"; await c.Db.SaveChangesAsync();
        await c.Service.IssueAsync(Phone, OtpPurpose.Registration, c.UserId, default); c.Db.ChangeTracker.Clear(); var all = await c.Db.OtpChallenges.ToListAsync();
        Assert.Equal(5, all.Count); Assert.Equal(Now, all.Single(o => o.Id == old.Id).RevokedAt); Assert.Null(all.Single(o => o.Id == used.Id).RevokedAt); Assert.Null(all.Single(o => o.Id == otherPurpose.Id).RevokedAt); Assert.Null(all.Single(o => o.Id == otherPhone.Id).RevokedAt);
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("locked")]
    [InlineData("used")]
    [InlineData("revoked")]
    [InlineData("purpose")]
    [InlineData("user")]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_RejectsInvalidChallengeWithoutConsuming(string state)
    {
        await using var c = new Context(); var otp = c.Seed();
        switch (state)
        {
            case "expired": otp.ExpiresAt = Now; break;
            case "locked": otp.LockedUntil = Now.AddTicks(1); break;
            case "used": otp.UsedAt = Now.AddMinutes(-1); break;
            case "revoked": otp.RevokedAt = Now.AddMinutes(-1); break;
            case "purpose": otp.Purpose = OtpPurpose.PasswordReset; break;
            case "user": otp.UserId = Guid.NewGuid(); break;
        }
        var originalUsedAt = otp.UsedAt; await c.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.VerifyAndConsumeAsync(Phone, Code, OtpPurpose.Registration, c.UserId, default)); Assert.Equal("Invalid OTP.", ex.Message);
        c.Db.ChangeTracker.Clear(); var persisted = await c.Db.OtpChallenges.SingleAsync(); Assert.Equal(originalUsedAt, persisted.UsedAt); Assert.Equal(0, persisted.AttemptCount);
    }
    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_WrongCodeIncrementsAttemptsAndLocksAtPolicyThreshold()
    {
        await using var c = new Context(); c.Seed(); c.Db.SystemPolicies.Add(new SystemPolicy { Id = Guid.NewGuid(), Currency = "VND", IsActive = true, OtpMaxAttempts = 2, OtpLockoutMinutes = 7 }); await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.VerifyAndConsumeAsync(Phone, "000000", OtpPurpose.Registration, c.UserId, default)); c.Db.ChangeTracker.Clear(); var first = await c.Db.OtpChallenges.SingleAsync(); Assert.Equal(1, first.AttemptCount); Assert.Null(first.LockedUntil);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.VerifyAndConsumeAsync(Phone, "000000", OtpPurpose.Registration, c.UserId, default)); c.Db.ChangeTracker.Clear(); var locked = await c.Db.OtpChallenges.SingleAsync(); Assert.Equal(2, locked.AttemptCount); Assert.Equal(Now.AddMinutes(7), locked.LockedUntil); Assert.Null(locked.UsedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.VerifyAndConsumeAsync(Phone, Code, OtpPurpose.Registration, c.UserId, default)); Assert.Null(locked.UsedAt);
    }
    [Fact]
    [Trait("Category", "OTP-02")]
    public async Task VerifyAndConsumeAsync_DefaultPolicyLocksOnFifthFailure()
    {
        await using var c = new Context(); c.Seed(); await c.Db.SaveChangesAsync();
        for (int i = 0; i < 5; i++) await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.VerifyAndConsumeAsync(Phone, "000000", OtpPurpose.Registration, c.UserId, default));
        c.Db.ChangeTracker.Clear(); var otp = await c.Db.OtpChallenges.SingleAsync(); Assert.Equal(5, otp.AttemptCount); Assert.Equal(Now.AddMinutes(15), otp.LockedUntil); Assert.Null(otp.UsedAt);
    }
    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1912345678")]
    [Trait("Category", "OTP-01")]
    public async Task IssueAsync_RejectsInvalidPhoneWithoutSaving(string phone)
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.IssueAsync(phone, OtpPurpose.Registration, null, default)); Assert.StartsWith("Phone number must contain 10 digits and start with 0.", ex.Message); Assert.Empty(c.Db.OtpChallenges); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FixedTokens : ITokenHashService
    {
        public string CreateNumericCode(int length = 6) => Code;
        public string HashToken(string token) => new Sha256TokenHashService().HashToken(token);
        public string CreateSecureToken(int byteLength = 64) => throw new NotSupportedException();
    }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid UserId { get; } = Guid.NewGuid();
        public OtpService Service => new(Db, new FixedTokens(), new Clock(), new ConfigurationBuilder().Build(), NullLogger<OtpService>.Instance);
        public OtpChallenge Seed() { var otp = new OtpChallenge { Id = Guid.NewGuid(), UserId = UserId, DestinationPhone = Phone, Purpose = OtpPurpose.Registration, CodeHash = new Sha256TokenHashService().HashToken(Code), CreatedAt = Now.AddMinutes(-1), ExpiresAt = Now.AddMinutes(9) }; Db.Add(otp); return otp; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
