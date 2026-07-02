using Serilog;
using smart_locking_be.API.Extensions;
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

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddControllers();
    builder.Services.AddCorsPolicy(builder.Configuration, builder.Environment);
    builder.Services.AddFileUploadLimits(builder.Configuration);
    builder.Services.AddJwtAuthentication(builder.Configuration);
    builder.Services.AddApiRateLimiting(builder.Configuration);
    builder.Services.AddApiRequestTimeouts(builder.Configuration);
    builder.Services.AddSwaggerDocumentation();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();

    app.UseRouting();
    app.UseCors();

    app.UseAuthentication();
    app.UseApiRateLimiting();
    app.UseApiRequestTimeouts();
    app.UseAuthorization();

    app.MapControllers();

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
