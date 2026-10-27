using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smart_locking_be.API;
using smart_locking_be.API.Middleware;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using System.Text.Json;

namespace smart_locking_be.Tests.API;

public sealed class ProblemDetailsTests
{
    [Fact]
    public void PublicRoutes_UseCanonicalOperationsAndImageEndpoints()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(smart_locking_be.API.Controllers.LockersController).Assembly);
        using var provider = services.BuildServiceProvider();
        var routes = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .Select(action => action.AttributeRouteInfo?.Template?.TrimStart('/').ToLowerInvariant())
            .Where(route => route is not null).ToArray();

        Assert.DoesNotContain(routes, route => route!.StartsWith("api/operations"));
        Assert.DoesNotContain("api/uploads/image", routes);
        Assert.DoesNotContain("api/uploads/return-image", routes);
        Assert.Contains("api/images", routes);
        Assert.Contains("api/uploads/images", routes);
        Assert.Contains("api/emergency-unlocks", routes);
        Assert.Contains("api/lockers/{id:guid}/status", routes);
        Assert.Contains("api/parcels/{id:guid}:transfertocollectionpoint", routes);
    }

    [Theory]
    [InlineData(401, "auth/invalid_credentials", "Invalid credentials.")]
    [InlineData(409, "locker/no_available_compartment", "No available compartment.")]
    [InlineData(400, "bluetooth/invalid_message", "Invalid device response.")]
    [InlineData(403, "bluetooth/access_denied", "Locker is outside operator scope.")]
    [InlineData(404, "bluetooth/not_found", "Reset request not found.")]
    [InlineData(409, "bluetooth/disabled", "Bluetooth is disabled by policy.")]
    [InlineData(503, "locker/open_failed", "Cannot connect to the locker.")]
    public async Task HandlerWritesDomainProblem(int status, string code, string message)
    {
        Exception exception = status switch
        {
            400 => new BadRequestException(code, message),
            401 => new UnauthorizedException(code, message),
            403 => new ForbiddenException(code, message),
            404 => new NotFoundException(code, message),
            503 => new ServiceUnavailableException(code, message),
            _ => new ConflictException(code, message)
        };

        using JsonDocument body = await HandleAsync(exception, status);
        JsonElement root = body.RootElement;
        Assert.Equal(status, root.GetProperty("status").GetInt32());
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Equal(message, root.GetProperty("detail").GetString());
        Assert.Equal($"https://errors.smartlocker.vn/{code.Replace('_', '-')}", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
        Assert.False(root.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task HandlerWritesFieldErrors()
    {
        using JsonDocument body = await HandleAsync(
            BadRequestException.For(new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["Phone number is invalid."],
                ["pinCode"] = ["PIN must contain six digits."]
            }), 400);

        JsonElement root = body.RootElement;
        Assert.Equal(CommonErrorCodes.InvalidInput, root.GetProperty("code").GetString());
        Assert.Equal(2, root.GetProperty("errors").EnumerateObject().Count());
        Assert.Equal("Phone number is invalid.", root.GetProperty("errors").GetProperty("phoneNumber")[0].GetString());
    }

    [Fact]
    public async Task HandlerHidesUnexpectedException()
    {
        using JsonDocument body = await HandleAsync(new Exception("database password"), 500);
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("detail").GetString());
        Assert.Equal(CommonErrorCodes.InternalError, body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("database password", body.RootElement.ToString());
    }

    [Fact]
    public async Task DirectProblemUsesProblemContentType()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        context.RequestAborted = new CancellationToken(true);
        await ProblemResponse.WriteAsync(context, 429, CommonErrorCodes.RateLimited, "Request limit exceeded.");

        Assert.Equal(429, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using JsonDocument body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(CommonErrorCodes.RateLimited, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void AutomaticModelValidationAddsCodeAndFieldErrors()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.Configuration["Jwt:Key"] = new string('x', 32);
        builder.Configuration["Jwt:Issuer"] = "test";
        builder.Configuration["Jwt:Audience"] = "test";
        builder.Services.AddApiServices(builder.Configuration, builder.Environment);
        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        DefaultHttpContext context = new() { RequestServices = provider };
        ModelStateDictionary state = new();
        state.AddModelError("phoneNumber", "Phone number is invalid.");
        state.AddModelError("pinCode", "PIN must contain six digits.");
        ActionContext action = new(context, new RouteData(), new ActionDescriptor(), state);

        var result = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value
            .InvalidModelStateResponseFactory(action);
        BadRequestObjectResult response = Assert.IsType<BadRequestObjectResult>(result);
        ValidationProblemDetails problem = Assert.IsType<ValidationProblemDetails>(response.Value);
        Assert.Equal(CommonErrorCodes.InvalidInput, problem.Extensions["code"]);
        Assert.Equal("Phone number is invalid.", Assert.Single(problem.Errors["phoneNumber"]));
        Assert.Equal("PIN must contain six digits.", Assert.Single(problem.Errors["pinCode"]));
        Assert.True(problem.Extensions.ContainsKey("traceId"));
        Assert.Contains("application/problem+json", response.ContentTypes);
    }

    private static async Task<JsonDocument> HandleAsync(Exception exception, int expectedStatus)
    {
        using ServiceProvider provider = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        DefaultHttpContext context = new() { RequestServices = provider };
        context.Request.Headers.Accept = "application/problem+json";
        context.Response.Body = new MemoryStream();
        GlobalExceptionHandler handler = new(provider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, default));
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
