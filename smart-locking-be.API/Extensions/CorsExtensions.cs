namespace smart_locking_be.API.Extensions;

public static class CorsExtensions
{
    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (environment.IsDevelopment())
                {
                    policy
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();

                    return;
                }

                string[] allowedOrigins = configuration
                    .GetSection("Cors:AllowedOrigins")
                    .Get<string[]>() ?? [];

                if (allowedOrigins.Length == 0)
                {
                    throw new InvalidOperationException("Cors:AllowedOrigins must be configured outside Development.");
                }

                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }
}
