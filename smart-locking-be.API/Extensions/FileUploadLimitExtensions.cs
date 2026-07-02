using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using smart_locking_be.API.Options;

namespace smart_locking_be.API.Extensions;

public static class FileUploadLimitExtensions
{
    public static IServiceCollection AddFileUploadLimits(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(FileUploadSettings.SectionName);
        FileUploadSettings settings = section.Get<FileUploadSettings>() ?? new FileUploadSettings();

        services.Configure<FileUploadSettings>(section);

        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = settings.GlobalMaxRequestBodyBytes;
        });

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = settings.MultipartBodyLengthLimitBytes;
        });

        return services;
    }
}
