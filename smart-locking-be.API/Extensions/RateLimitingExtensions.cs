using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.API.Options;

namespace smart_locking_be.API.Extensions;

public static class RateLimitingExtensions
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(RateLimitSettings.SectionName);
        RateLimitSettings settings = section.Get<RateLimitSettings>() ?? new RateLimitSettings();

        services.Configure<RateLimitSettings>(section);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                ILogger logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");

                logger.LogWarning(
                    "Rate limit rejected request. Path: {Path}",
                    context.HttpContext.Request.Path);

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    status = StatusCodes.Status429TooManyRequests,
                    title = "Too many requests",
                    message = "Request limit exceeded. Please try again later."
                }, cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetTokenBucketLimiter(
                    GetPartitionKey(httpContext),
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = settings.Global.TokenLimit,
                        TokensPerPeriod = settings.Global.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(settings.Global.ReplenishmentPeriodSeconds),
                        QueueLimit = settings.Global.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.PublicApi, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PublicApi.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.PublicApi.WindowSeconds),
                        QueueLimit = settings.PublicApi.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.Auth, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.Auth.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.Auth.WindowSeconds),
                        SegmentsPerWindow = 6,
                        QueueLimit = settings.Auth.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.Search, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.Search.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.Search.WindowSeconds),
                        SegmentsPerWindow = settings.Search.SegmentsPerWindow,
                        QueueLimit = settings.Search.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.Upload, httpContext =>
                RateLimitPartition.GetConcurrencyLimiter(
                    GetPartitionKey(httpContext),
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = settings.Upload.PermitLimit,
                        QueueLimit = settings.Upload.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

            options.AddPolicy(RateLimitPolicyNames.HeavyAction, httpContext =>
                RateLimitPartition.GetConcurrencyLimiter(
                    GetPartitionKey(httpContext),
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = settings.HeavyAction.PermitLimit,
                        QueueLimit = settings.HeavyAction.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

            options.AddPolicy(RateLimitPolicyNames.DeviceCommand, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetDeviceCommandPartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.DeviceCommand.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.DeviceCommand.WindowSeconds),
                        SegmentsPerWindow = settings.DeviceCommand.SegmentsPerWindow,
                        QueueLimit = settings.DeviceCommand.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    public static IApplicationBuilder UseApiRateLimiting(this IApplicationBuilder app)
    {
        app.UseRateLimiter();

        return app;
    }

    private static string GetDeviceCommandPartitionKey(HttpContext httpContext)
    {
        string partitionKey = GetPartitionKey(httpContext);

        if (httpContext.Request.RouteValues.TryGetValue("deviceId", out object? deviceId) && deviceId is not null)
        {
            return $"{partitionKey}:device:{deviceId}";
        }

        if (httpContext.Request.RouteValues.TryGetValue("lockId", out object? lockId) && lockId is not null)
        {
            return $"{partitionKey}:lock:{lockId}";
        }

        return partitionKey;
    }

    private static string GetPartitionKey(HttpContext httpContext)
    {
        string? apiKey = httpContext.Request.Headers[ApiKeyHeaderName].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return $"api-key:{HashValue(apiKey)}";
        }

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            string? userId =
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                httpContext.User.FindFirstValue("sub") ??
                httpContext.User.FindFirstValue("userId");

            if (!string.IsNullOrWhiteSpace(userId))
            {
                return $"user:{userId}";
            }
        }

        string ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return $"ip:{ipAddress}";
    }

    private static string HashValue(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexString(bytes);
    }
}
