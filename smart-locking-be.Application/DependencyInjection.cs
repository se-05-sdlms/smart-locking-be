using Microsoft.Extensions.DependencyInjection;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Application.Services;

namespace smart_locking_be.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICompartmentResetService, CompartmentResetService>();
        // Use case chỉ phụ thuộc interface; các adapter EF/MQTT được đăng ký ở Infrastructure.
        services.AddScoped<IBluetoothWorkflowService, BluetoothWorkflowService>();

        return services;
    }
}
