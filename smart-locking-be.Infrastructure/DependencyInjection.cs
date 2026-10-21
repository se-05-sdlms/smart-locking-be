using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using smart_locking_be.Application.Interfaces;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Options;
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
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHashService, Pbkdf2PasswordHashService>();
        services.AddScoped<ITokenHashService, Sha256TokenHashService>();
        services.AddScoped<IResidentService, ResidentService>();
        services.Configure<Options.PayOSOptions>(options =>
        {
            options.ClientId = configuration["PayOS:ClientId"] ?? string.Empty;
            options.ApiKey = configuration["PayOS:ApiKey"] ?? string.Empty;
            options.ChecksumKey = configuration["PayOS:ChecksumKey"] ?? string.Empty;
            options.ReturnUrl = configuration["PayOS:ReturnUrl"] ?? string.Empty;
            options.CancelUrl = configuration["PayOS:CancelUrl"] ?? string.Empty;
        });
        services.AddScoped<ISubscriptionPlanService, SubscriptionPlanService>();
        services.AddScoped<IResidentSubscriptionService, ResidentSubscriptionService>();
        services.AddScoped<IPaymentGatewayService, PayOSPaymentGatewayService>();
        services.AddScoped<ILockerService, LockerService>();
        services.AddScoped<ILockerAccessService, LockerAccessService>();
        services.AddScoped<ILockerCommandDispatcher, MqttLockerCommandDispatcher>();
        services.AddScoped<ICompartmentAllocationService, CompartmentAllocationService>();
        services.AddScoped<IDeliveryRequestService, DeliveryRequestService>();
        services.AddScoped<IParcelService, ParcelService>();
        services.AddScoped<IParcelPickupService, ParcelPickupService>();
        services.AddScoped<IOverdueChargeService, OverdueChargeService>();
        services.AddScoped<IIncidentService, IncidentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDeviceInstallationService, DeviceInstallationService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        IConfigurationSection storageSection = configuration.GetSection(SupabaseStorageOptions.SectionName);
        services.AddOptions<SupabaseStorageOptions>()
            .Configure(options =>
            {
                options.Url = storageSection["Url"] ?? string.Empty;
                options.ApiKey = storageSection["ApiKey"] ?? string.Empty;
                options.BucketName = storageSection["BucketName"] ?? "smart-locking-images";
                options.Folder = storageSection["Folder"] ?? "parcels";
            })
            .Validate(options =>
                string.IsNullOrWhiteSpace(options.Url) || string.IsNullOrWhiteSpace(options.ApiKey) ||
                (Uri.TryCreate(options.Url, UriKind.Absolute, out Uri? url) &&
                 (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) &&
                 !string.IsNullOrEmpty(url.Host) && string.IsNullOrEmpty(url.UserInfo) &&
                 string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment) &&
                 !options.ApiKey.Any(char.IsWhiteSpace) && !options.ApiKey.Any(char.IsControl) &&
                 !string.IsNullOrWhiteSpace(options.BucketName) &&
                 options.BucketName is not "." and not ".." && !options.BucketName.Contains('/') &&
                 !options.BucketName.Contains('\\') &&
                 !options.Folder.Contains('\\') &&
                 options.Folder.Split('/').All(segment => segment is not "." and not "..")),
                "Supabase storage URL, API key, bucket, or folder configuration is invalid.")
            .ValidateOnStart();
        services.AddHttpClient<SupabaseImageStorageService>(client =>
            client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IPushNotificationService, ExpoPushNotificationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminService, AdminService>();

        services.AddScoped<IReturnRequestService, ReturnRequestService>();

        services.AddScoped<IReturnPickupSessionService, ReturnRequestService>();

        services.AddScoped<IEmergencyUnlockService, EmergencyUnlockService>();

        services.AddScoped<IMaintenanceService, MaintenanceService>();

        services.AddScoped<IAuditLogService, AuditLogService>();

        services.AddScoped<IOperationalRecordService, OperationalRecordService>();

        services.AddScoped<IReportService, ReportService>();

        services.AddScoped<ISystemPolicyService, SystemPolicyService>();

        services.AddHostedService<MqttLockerListenerService>();
        services.AddScoped<LockerConnectionMonitor>();
        services.AddHostedService<LockerConnectionWorker>();

        services.AddScoped<INotificationRuleService, NotificationRuleService>();

        services.AddScoped<IParcelReminderService, ParcelReminderService>();

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
