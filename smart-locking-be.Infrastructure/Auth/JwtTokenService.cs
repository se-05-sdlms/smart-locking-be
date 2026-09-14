using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using smart_locking_be.Application.Auth;
using smart_locking_be.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace smart_locking_be.Infrastructure.Auth;

public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user)
    {
        IConfigurationSection jwtSection = configuration.GetSection("Jwt");
        string key = GetRequiredValue(jwtSection, "Key");
        string issuer = GetRequiredValue(jwtSection, "Issuer");
        string audience = GetRequiredValue(jwtSection, "Audience");
        int accessTokenMinutes = int.TryParse(jwtSection["AccessTokenMinutes"], out int minutes) ? minutes : 15;
        byte[] signingKey = Encoding.UTF8.GetBytes(key);

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes for HMAC SHA-256.");
        }

        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(accessTokenMinutes);
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            claims.Add(new Claim("phone_number", user.PhoneNumber));
        }

        JwtSecurityToken token = new(
            issuer,
            audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static string GetRequiredValue(IConfiguration configuration, string key)
    {
        string? value = configuration[key];

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Jwt:{key} is not configured.")
            : value;
    }
}
