using Microsoft.Extensions.Configuration;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace smart_locking_be.Tests.Auth;

public sealed class JwtTokenServiceTests
{
    private static IConfiguration CreateConfig(string? key = "test-secret-key-with-more-than-32-bytes", string? issuer = "smart-locking-be", string? audience = "smart-locking-clients", string? minutes = "15")
    {
        var dict = new Dictionary<string, string?>();
        if (key != null) dict["Jwt:Key"] = key;
        if (issuer != null) dict["Jwt:Issuer"] = issuer;
        if (audience != null) dict["Jwt:Audience"] = audience;
        if (minutes != null) dict["Jwt:AccessTokenMinutes"] = minutes;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static User CreateUser(UserRole role = UserRole.Resident, string? email = "resident@example.com", string? phone = "0912345678")
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PhoneNumber = phone,
            Role = role,
            Status = UserStatus.Active
        };
    }

    #region JWT-01: JwtTokenService.CreateAccessToken
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC01()
    {
        var service = new JwtTokenService(CreateConfig());
        var user = CreateUser();
        var (token, expiresAt) = service.CreateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC02()
    {
        string key32 = new string('a', 32);
        var service = new JwtTokenService(CreateConfig(key: key32));
        var user = CreateUser();
        var (token, expiresAt) = service.CreateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC03()
    {
        string key31 = new string('a', 31);
        var service = new JwtTokenService(CreateConfig(key: key31));
        var user = CreateUser();

        var ex = Assert.Throws<InvalidOperationException>(() => service.CreateAccessToken(user));
        Assert.Contains("Jwt:Key must be at least 32 bytes", ex.Message);
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC04()
    {
        var service = new JwtTokenService(CreateConfig(key: null));
        var user = CreateUser();

        var ex = Assert.Throws<InvalidOperationException>(() => service.CreateAccessToken(user));
        Assert.Equal("Jwt:Key is not configured.", ex.Message);
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC05()
    {
        var service = new JwtTokenService(CreateConfig(issuer: null));
        var user = CreateUser();

        var ex = Assert.Throws<InvalidOperationException>(() => service.CreateAccessToken(user));
        Assert.Equal("Jwt:Issuer is not configured.", ex.Message);
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC06()
    {
        var service = new JwtTokenService(CreateConfig(audience: null));
        var user = CreateUser();

        var ex = Assert.Throws<InvalidOperationException>(() => service.CreateAccessToken(user));
        Assert.Equal("Jwt:Audience is not configured.", ex.Message);
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC07()
    {
        var service = new JwtTokenService(CreateConfig(minutes: "invalid"));
        var user = CreateUser();
        var (token, expiresAt) = service.CreateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(token));
        // Default 15 minutes
        Assert.True(expiresAt > DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC08()
    {
        var service = new JwtTokenService(CreateConfig());
        var user = CreateUser(email: null, phone: null);
        var (token, expiresAt) = service.CreateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC09()
    {
        var service = new JwtTokenService(CreateConfig());
        var user = CreateUser(role: UserRole.LockerOperator);
        var (token, expiresAt) = service.CreateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }
    #endregion
}
