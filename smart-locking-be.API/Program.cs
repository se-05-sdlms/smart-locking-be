using Serilog;
using smart_locking_be.API;
using smart_locking_be.Application;
using smart_locking_be.Infrastructure;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // 1. Gom đăng ký Service Dependency Injection theo từng tầng kiến trúc
    builder.Services
        .AddApiServices(builder.Configuration, builder.Environment)
        .AddApplication()
        .AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    // 2. Gom cấu hình HTTP Request Pipeline trong DependencyInjection.cs của tầng API
    app.UseApiPipeline();

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Application terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

