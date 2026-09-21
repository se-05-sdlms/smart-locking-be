using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using smart_locking_be.Application.Auth;
using smart_locking_be.Application.Interfaces;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        // 1. Đăng ký ApplicationDbContext với PostgreSQL (Npgsql)
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // 2. Đăng ký Service mẫu (Interface ở Application, Implementation ở Infrastructure tiêm DbContext trực tiếp)
        services.AddScoped<IService, Service>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHashService, Pbkdf2PasswordHashService>();
        services.AddScoped<ITokenHashService, Sha256TokenHashService>();
        services.AddScoped<IResidentService, ResidentService>();
        services.AddScoped<ILockerService, LockerService>();
        services.AddScoped<IUserService, UserService>();

        // HƯỚNG DẪN ĐĂNG KÝ SERVICE TRONG TƯƠNG LAI:
        // Các Service thực thi nghiệp vụ tiêm trực tiếp ApplicationDbContext được ghép cặp như sau:
        //
        // Ví dụ AddScoped:
        // services.AddScoped<IAuthService, AuthService>();
        // services.AddScoped<ILockerService, LockerService>();
        // services.AddScoped<IParcelService, ParcelService>();

        return services;
    }
}
