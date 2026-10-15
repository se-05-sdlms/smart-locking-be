using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;

namespace smart_locking_be.Tests.Auth;

public sealed class JwtTokenServiceTests
{
    private const string Key = "test-secret-key-with-more-than-32-bytes";
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC01() => AssertToken(Key);
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC02() => AssertToken(new string('a', 32));
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC03() => AssertConfigurationError("Key", new string('a', 31), "Jwt:Key must be at least 32 bytes for HMAC SHA-256.");
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC04() => AssertConfigurationError("Key", null, "Jwt:Key is not configured.");
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC05() => AssertConfigurationError("Issuer", null, "Jwt:Issuer is not configured.");
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC06() => AssertConfigurationError("Audience", null, "Jwt:Audience is not configured.");
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC07() => AssertToken(Key, minutes: "invalid");
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC08() => AssertToken(Key, contacts: false);
    [Fact]
    [Trait("Category", "JWT-01")]
    public void CreateAccessToken_UTC09() => AssertToken(Key, mustChange: true);

    private static User User(bool contacts = true, bool mustChange = false) => new() { Id = Guid.NewGuid(), Role = UserRole.Resident, Email = contacts ? "resident@example.com" : null, PhoneNumber = contacts ? "0912345678" : null, MustChangePassword = mustChange };
    private static Dictionary<string, string?> Config(string key = Key, string minutes = "15") => new() { ["Jwt:Key"] = key, ["Jwt:Issuer"] = "smart-locking-be", ["Jwt:Audience"] = "smart-locking-clients", ["Jwt:AccessTokenMinutes"] = minutes };
    private static void AssertConfigurationError(string setting, string? value, string message)
    { var config = Config(); config["Jwt:" + setting] = value; var service = new JwtTokenService(new ConfigurationBuilder().AddInMemoryCollection(config).Build()); var ex = Assert.Throws<InvalidOperationException>(() => service.CreateAccessToken(User())); Assert.Equal(message, ex.Message); }
    private static void AssertToken(string key, string minutes = "15", bool contacts = true, bool mustChange = false)
    {
        var user = User(contacts, mustChange); var service = new JwtTokenService(new ConfigurationBuilder().AddInMemoryCollection(Config(key, minutes)).Build()); var before = DateTimeOffset.UtcNow;
        var result = service.CreateAccessToken(user); var after = DateTimeOffset.UtcNow;
        Assert.InRange(result.ExpiresAt, before.AddMinutes(15), after.AddMinutes(15)); var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(result.Token, new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidateIssuer = true, ValidIssuer = "smart-locking-be", ValidateAudience = true, ValidAudience = "smart-locking-clients", ValidateLifetime = true, ClockSkew = TimeSpan.Zero }, out var validated);
        var jwt = Assert.IsType<JwtSecurityToken>(validated); Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg); Assert.Equal(user.Id.ToString(), jwt.Subject); Assert.Equal(user.Id.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value); Assert.Equal(nameof(UserRole.Resident), principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal(mustChange ? "true" : "false", jwt.Claims.Single(c => c.Type == "must_change_password").Value); Assert.True(Guid.TryParse(jwt.Id, out _)); Assert.Equal(result.ExpiresAt.ToUnixTimeSeconds(), new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds());
        Assert.Equal(contacts ? user.Email : null, principal.FindFirst(ClaimTypes.Email)?.Value); Assert.Equal(contacts ? user.PhoneNumber : null, principal.FindFirst("phone_number")?.Value);
        var next = handler.ReadJwtToken(service.CreateAccessToken(user).Token); Assert.NotEqual(jwt.Id, next.Id);
    }
}
