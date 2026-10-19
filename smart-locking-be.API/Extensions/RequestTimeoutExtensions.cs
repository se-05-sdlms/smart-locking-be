using Microsoft.AspNetCore.Http.Timeouts;
using smart_locking_be.API.Constants;
using smart_locking_be.API.Options;
using smart_locking_be.API.Middleware;

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
            RequestTimeoutPolicy Policy(int seconds) => new()
            {
                Timeout = TimeSpan.FromSeconds(seconds),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
                WriteTimeoutResponse = context => ProblemResponse.WriteAsync(context,
                    StatusCodes.Status504GatewayTimeout, CommonErrorCodes.RequestTimedOut,
                    "Request timed out.")
            };

            options.DefaultPolicy = Policy(settings.DefaultSeconds);

            options.AddPolicy(RequestTimeoutPolicyNames.Default, Policy(settings.DefaultSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.Auth, Policy(settings.AuthSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.Search, Policy(settings.SearchSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.Upload, Policy(settings.UploadSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.HeavyAction, Policy(settings.HeavyActionSeconds));
            options.AddPolicy(RequestTimeoutPolicyNames.DeviceCommand, Policy(settings.DeviceCommandSeconds));
        });

        return services;
    }

    public static IApplicationBuilder UseApiRequestTimeouts(this IApplicationBuilder app)
    {
        app.UseRequestTimeouts();

        return app;
    }
}
