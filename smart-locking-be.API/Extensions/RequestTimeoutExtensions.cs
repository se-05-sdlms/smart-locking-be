using Microsoft.AspNetCore.Http.Timeouts;
using smart_locking_be.API.Constants;
using smart_locking_be.API.Options;

namespace smart_locking_be.API.Extensions;

public static class RequestTimeoutExtensions
{
    public static IServiceCollection AddApiRequestTimeouts(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(RequestTimeoutSettings.SectionName);
        RequestTimeoutSettings settings = section.Get<RequestTimeoutSettings>() ?? new RequestTimeoutSettings();

        services.Configure<RequestTimeoutSettings>(section);

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(settings.DefaultSeconds),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
            };

            options.AddPolicy(RequestTimeoutPolicyNames.Default, TimeSpan.FromSeconds(settings.DefaultSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.Auth, TimeSpan.FromSeconds(settings.AuthSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.Search, TimeSpan.FromSeconds(settings.SearchSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.Upload, TimeSpan.FromSeconds(settings.UploadSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.HeavyAction, TimeSpan.FromSeconds(settings.HeavyActionSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.DeviceCommand, TimeSpan.FromSeconds(settings.DeviceCommandSeconds));
        });

        return services;
    }

    public static IApplicationBuilder UseApiRequestTimeouts(this IApplicationBuilder app)
    {
        app.UseRequestTimeouts();

        return app;
    }
}
