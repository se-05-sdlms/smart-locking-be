using smart_locking_be.Application.Exceptions;
using smart_locking_be.Infrastructure.Auth;

namespace smart_locking_be.Tests.Auth;

public sealed class Pbkdf2PasswordHashServiceTests
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

}
