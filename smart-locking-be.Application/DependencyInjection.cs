using Microsoft.Extensions.DependencyInjection;

namespace smart_locking_be.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
