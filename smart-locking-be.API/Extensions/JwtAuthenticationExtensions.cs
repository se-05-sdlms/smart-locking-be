using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using smart_locking_be.API.Authorization;
using System.Security.Claims;
using System.Text;

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

        // 1. Đăng ký Authentication với JWT Bearer Schema
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
                    ClockSkew = TimeSpan.Zero,
                    // Ánh ánh claim chứa vai trò trong token với System.Security.Claims.ClaimTypes.Role
                    RoleClaimType = ClaimTypes.Role
                };
            });

        // 2. Đăng ký Authorization Policies theo vai trò (Role-based policies)
        // Dùng ApiPolicies hằng số để áp dụng [Authorize(Policy = ApiPolicies.Administrator)] trên Controller/Endpoint
        services.AddAuthorization(options =>
        {
            // Policy dành cho Quản trị viên
            options.AddPolicy(ApiPolicies.Administrator, policy =>
                policy.RequireRole(ApiPolicies.Administrator));

            // Policy dành cho Cư dân
            options.AddPolicy(ApiPolicies.Resident, policy =>
                policy.RequireRole(ApiPolicies.Resident));

            // Policy dành cho Nhân viên vận hành
            options.AddPolicy(ApiPolicies.LockerOperator, policy =>
                policy.RequireRole(ApiPolicies.LockerOperator));
        });

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
