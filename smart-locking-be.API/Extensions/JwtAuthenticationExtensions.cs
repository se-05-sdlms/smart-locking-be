using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace smart_locking_be.API.Extensions;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection jwtSection = configuration.GetSection("Jwt");
        string key = GetRequiredValue(jwtSection, "Key");
        string issuer = GetRequiredValue(jwtSection, "Issuer");
        string audience = GetRequiredValue(jwtSection, "Audience");
        byte[] signingKey = Encoding.UTF8.GetBytes(key);

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes for HMAC SHA-256.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization();

        return services;
    }

    private static string GetRequiredValue(IConfiguration configuration, string key)
    {
        string? value = configuration[key];

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Jwt:{key} is not configured.")
            : value;
    }
}
