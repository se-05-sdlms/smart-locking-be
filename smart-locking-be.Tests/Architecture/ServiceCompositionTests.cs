using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using smart_locking_be.API;
using smart_locking_be.API.Controllers;
using smart_locking_be.Application;
using smart_locking_be.Infrastructure;

namespace smart_locking_be.Tests.Architecture;

public sealed class ServiceCompositionTests
{
    [Fact]
    public async Task All_controllers_and_workers_resolve_from_application_registration()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ImagesController).Assembly.FullName,
            EnvironmentName = "Testing",
            ContentRootPath = Path.GetTempPath()
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=composition_only;Username=test;Password=test",
            ["Jwt:Key"] = "test-signing-key-with-at-least-thirty-two-bytes",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test"
            ,
            ["Cors:AllowedOrigins:0"] = "https://test.invalid"
        });
        builder.Services.AddApiServices(builder.Configuration, builder.Environment).AddApplication().AddInfrastructure(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(ImagesController).Assembly).AddControllersAsServices();
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        foreach (Type controller in typeof(ImagesController).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)))
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(controller));
        Assert.NotEmpty(app.Services.GetServices<IHostedService>());
    }
}
