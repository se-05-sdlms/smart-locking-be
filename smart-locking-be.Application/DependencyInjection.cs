using Microsoft.Extensions.DependencyInjection;

namespace smart_locking_be.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Tầng Application chứa interface, DTOs, Validators và các nghiệp vụ Use Cases / Application Services.
        // HƯỚNG DẪN ĐĂNG KÝ SERVICE TRONG TƯƠNG LAI:
        // Khi tạo Service nghiệp vụ (Ví dụ: AuthService, ParcelService, LockerService), hãy đăng ký AddScoped tại đây:
        //
        // Ví dụ:
        // services.AddScoped<IAuthService, AuthService>();
        // services.AddScoped<IParcelService, ParcelService>();
        // services.AddScoped<ILockerService, LockerService>();
        // services.AddScoped<IBuildingService, BuildingService>();

        return services;
    }
}
