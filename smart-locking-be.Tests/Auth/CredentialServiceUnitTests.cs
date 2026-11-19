using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Auth;

public sealed class CredentialServiceUnitTests
{
    [Fact]
    [Trait("Category", "AUTH-10")]
    public void PasswordHash_RoundTripsAndUsesDifferentSalt()
    {
        var service = new Pbkdf2PasswordHashService();

        string first = service.HashPassword("P@ssw0rd123");
        string second = service.HashPassword("P@ssw0rd123");

        Assert.NotEqual(first, second);
        Assert.True(service.VerifyPassword("P@ssw0rd123", first));
        Assert.False(service.VerifyPassword("wrong", first));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "AUTH-10")]
    public void PasswordHash_RejectsEmptyPassword(string? password)
    {
        var service = new Pbkdf2PasswordHashService();

        Assert.Throws<BadRequestException>(() => service.HashPassword(password!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2-SHA256$150000$bad$bad")]
    [InlineData("PBKDF2-SHA256$150000$$")]
    [Trait("Category", "AUTH-10")]
    public void PasswordHash_ReturnsFalseForMalformedHash(string hash)
    {
        var service = new Pbkdf2PasswordHashService();

        Assert.False(service.VerifyPassword("P@ssw0rd123", hash));
    }

    [Fact]
    [Trait("Category", "AUTH-10")]
    public void TokenHash_IsDeterministicAndNumericCodeHasRequestedLength()
    {
        var service = new Sha256TokenHashService();

        Assert.Equal(service.HashToken("token"), service.HashToken("token"));
        Assert.NotEqual(service.HashToken("token"), service.HashToken("other"));
        Assert.Matches("^[0-9]{6}$", service.CreateNumericCode());
        Assert.Matches("^[0-9]{4}$", service.CreateNumericCode(4));
        Assert.NotEqual(service.CreateSecureToken(), service.CreateSecureToken());
    }

    [Fact]
    [Trait("Category", "AUTH-11")]
    public async Task RegistrationLockers_ReturnsOnlyOperationalLockersOrderedByCode()
    {
        await using var db = CreateDbContext();
        db.Lockers.AddRange(
            Locker("B-02", LockerOperationalStatus.Operational),
            Locker("A-01", LockerOperationalStatus.Operational),
            Locker("C-03", LockerOperationalStatus.OutOfService));
        await db.SaveChangesAsync();

        var service = new AuthService(
            db,
            new Pbkdf2PasswordHashService(),
            new Sha256TokenHashService(),
            null!,
            null!,
            Configuration());

        var result = await service.GetRegistrationLockersAsync(default);

        Assert.Collection(result,
            item => Assert.Equal("A-01", item.Code),
            item => Assert.Equal("B-02", item.Code));
    }

    [Fact]
    [Trait("Category", "AUTH-11")]
    public async Task RegistrationLockers_ReturnsEmptyWhenNoOperationalLockerExists()
    {
        await using var db = CreateDbContext();
        db.Lockers.Add(Locker("A-01", LockerOperationalStatus.Inactive));
        await db.SaveChangesAsync();

        var service = new AuthService(
            db,
            new Pbkdf2PasswordHashService(),
            new Sha256TokenHashService(),
            null!,
            null!,
            Configuration());

        Assert.Empty(await service.GetRegistrationLockersAsync(default));
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Locker Locker(string code, LockerOperationalStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Address = $"{code} address",
        OperationalStatus = status,
    };

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>())
        .Build();
}
