using Serilog;
using smart_locking_be.API.Extensions;
using smart_locking_be.API.Services;

namespace smart_locking_be.API;

public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký tất cả dịch vụ thuộc tầng API (Controllers, CORS, Swagger, Auth, RateLimiting, RequestTimeouts, FileUpload).
    /// </summary>
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // 1. Controllers & API Behavior
        services.AddControllers();

        // 2. CORS Policy Configuration
        services.AddCorsPolicy(configuration, environment);

        // 3. File Upload Limits Settings
        services.AddFileUploadLimits(configuration);

        // 4. JWT Authentication & Role Authorization Policies
        services.AddJwtAuthentication(configuration);

        // 5. API Rate Limiting Policies
        services.AddApiRateLimiting(configuration);

        // 6. Request Timeout Policies
        services.AddApiRequestTimeouts(configuration);

        // 7. Swagger / OpenAPI Documentation
        services.AddSwaggerDocumentation();

        // 8. Delivery request session expiration
        services.AddHostedService<DeliveryRequestExpirationWorker>();

        // HƯỚNG DẪN ĐĂNG KÝ SERVICE CẤP API TRONG TƯƠNG LAI:
        // Khi cần tạo các service hoặc BackgroundWorker ở tầng API, thực hiện đăng ký tại đây:
        // - AddScoped (cho request-scoped logic):
        //   services.AddScoped<IWebHookService, WebHookService>();
        // - AddSingleton (cho in-memory cache/state):
        //   services.AddSingleton<IConnectionManager, ConnectionManager>();
        // - AddHostedService (cho background job/worker):
        //   services.AddHostedService<LockerStatusBackgroundWorker>();

        return services;
    }

    /// <summary>
    /// Cấu hình HTTP Request Pipeline theo đúng thứ tự middleware tiêu chuẩn của ASP.NET Core.
    /// </summary>
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        // 1. Swagger UI chỉ bật ở môi trường Development
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // 2. Log request bằng Serilog
        app.UseSerilogRequestLogging();

        // 3. Chuyển hướng HTTPS
        app.UseHttpsRedirection();

        // 4. Routing
        app.UseRouting();

        // 5. Cross-Origin Resource Sharing (CORS)
        app.UseCors();

        // 6. Xác thực danh tính người dùng (Authentication)
        app.UseAuthentication();

        // 7. Giới hạn số lượng request (Rate Limiting)
        app.UseApiRateLimiting();

        // 8. Giới hạn thời gian xử lý request (Request Timeout)
        app.UseApiRequestTimeouts();

        // 9. Phân quyền truy cập theo Role/Policy (Authorization)
        app.UseAuthorization();

        // 10. Ánh xạ các Controller endpoints
        app.MapControllers();

        return app;
    }
}
