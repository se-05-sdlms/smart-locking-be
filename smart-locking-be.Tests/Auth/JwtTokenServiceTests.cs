using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace smart_locking_be.Tests.Auth;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_CanBeValidatedWithConfiguredIssuerAudienceAndRole()
    {
        const string key = "test-secret-key-with-at-least-32-bytes";
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = key,
                ["Jwt:Issuer"] = "smart-locking-be",
                ["Jwt:Audience"] = "smart-locking-clients",
                ["Jwt:AccessTokenMinutes"] = "15"
            })
            .Build();
        User user = new()
        {
            Id = Guid.NewGuid(),
            Email = "RESIDENT@EXAMPLE.COM",
            Role = UserRole.Resident,
            Status = UserStatus.Active
        };

        (string token, DateTimeOffset expiresAt) = new JwtTokenService(configuration).CreateAccessToken(user);

        ClaimsPrincipal principal = new JwtSecurityTokenHandler().ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "smart-locking-be",
                ValidAudience = "smart-locking-clients",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ClockSkew = TimeSpan.Zero,
                RoleClaimType = ClaimTypes.Role
            },
            out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(UserRole.Resident.ToString(), principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
    }
}
