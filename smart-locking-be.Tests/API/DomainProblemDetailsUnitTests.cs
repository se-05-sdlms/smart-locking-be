using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.API.Middleware;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using System.Text.Json;

namespace smart_locking_be.Tests.API;

public sealed class DomainProblemDetailsUnitTests
{
    private static async Task<(DefaultHttpContext Context, JsonDocument Body)> ExecuteHandlerAsync(Exception exception)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();

        DefaultHttpContext context = new()
        {
            RequestServices = provider,
            TraceIdentifier = $"test-trace-{Guid.NewGuid():N}"
        };
        context.Request.Headers.Accept = "application/problem+json";
        context.Response.Body = new MemoryStream();

        GlobalExceptionHandler handler = new(
            provider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        bool handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        Assert.True(handled);

        context.Response.Body.Position = 0;
        JsonDocument jsonDocument = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, jsonDocument);
    }

    [Fact]
    public async Task NotFoundException_TransformsTo_404ProblemDetails()
    {
        // Arrange
        const string errorCode = "locker/not_found";
        const string errorMessage = "Locker with specified identifier was not found.";
        var exception = new NotFoundException(errorCode, errorMessage);

        // Act
        var (context, body) = await ExecuteHandlerAsync(exception);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        JsonElement root = body.RootElement;
        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, root.GetProperty("code").GetString());
        Assert.Equal(errorMessage, root.GetProperty("detail").GetString());
        Assert.Equal("Not Found", root.GetProperty("title").GetString());
        Assert.Equal("https://errors.smartlocker.vn/locker/not-found", root.GetProperty("type").GetString());
        Assert.Equal(context.TraceIdentifier, root.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task ConflictException_TransformsTo_409ProblemDetails()
    {
        // Arrange
        const string errorCode = "booking/compartment_already_reserved";
        const string errorMessage = "The target compartment has already been occupied.";
        var exception = new ConflictException(errorCode, errorMessage);

        // Act
        var (context, body) = await ExecuteHandlerAsync(exception);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        JsonElement root = body.RootElement;
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, root.GetProperty("code").GetString());
        Assert.Equal(errorMessage, root.GetProperty("detail").GetString());
        Assert.Equal("Conflict", root.GetProperty("title").GetString());
        Assert.Equal("https://errors.smartlocker.vn/booking/compartment-already-reserved", root.GetProperty("type").GetString());
        Assert.Equal(context.TraceIdentifier, root.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task BadRequestException_WithFieldErrors_TransformsTo_400ValidationProblemDetails()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            ["phoneNumber"] = ["Phone number format is invalid."],
            ["pinCode"] = ["PIN must be 6 digits.", "PIN cannot be trivial."]
        };
        var exception = BadRequestException.For(errors);

        // Act
        var (context, body) = await ExecuteHandlerAsync(exception);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        JsonElement root = body.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal(CommonErrorCodes.InvalidInput, root.GetProperty("code").GetString());
        Assert.Equal("One or more validation errors occurred.", root.GetProperty("title").GetString());
        Assert.Equal("https://errors.smartlocker.vn/validation/invalid-input", root.GetProperty("type").GetString());

        JsonElement errorsElement = root.GetProperty("errors");
        Assert.True(errorsElement.TryGetProperty("phoneNumber", out JsonElement phoneErrors));
        Assert.Single(phoneErrors.EnumerateArray());
        Assert.Equal("Phone number format is invalid.", phoneErrors[0].GetString());

        Assert.True(errorsElement.TryGetProperty("pinCode", out JsonElement pinErrors));
        Assert.Equal(2, pinErrors.GetArrayLength());
        Assert.Equal("PIN must be 6 digits.", pinErrors[0].GetString());
        Assert.Equal("PIN cannot be trivial.", pinErrors[1].GetString());
    }

    [Fact]
    public async Task BadRequestException_WithoutFieldErrors_TransformsTo_400ProblemDetails()
    {
        // Arrange
        const string errorCode = "request/invalid_format";
        const string errorMessage = "The payload does not conform to required schema.";
        var exception = new BadRequestException(errorCode, errorMessage);

        // Act
        var (context, body) = await ExecuteHandlerAsync(exception);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        JsonElement root = body.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, root.GetProperty("code").GetString());
        Assert.Equal(errorMessage, root.GetProperty("detail").GetString());
        Assert.Equal("Bad Request", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ForbiddenException_TransformsTo_403ProblemDetails()
    {
        // Arrange
        const string errorCode = "auth/forbidden";
        const string errorMessage = "You do not have administrative privileges.";
        var exception = new ForbiddenException(errorCode, errorMessage);

        // Act
        var (context, body) = await ExecuteHandlerAsync(exception);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        JsonElement root = body.RootElement;
        Assert.Equal(403, root.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, root.GetProperty("code").GetString());
        Assert.Equal(errorMessage, root.GetProperty("detail").GetString());
        Assert.Equal("Forbidden", root.GetProperty("title").GetString());
        Assert.Equal("https://errors.smartlocker.vn/auth/forbidden", root.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UnauthorizedException_TransformsTo_401ProblemDetails_AndSetsWwwAuthenticateHeader()
    {
        // Arrange
        const string errorCode = "auth/unauthorized";
        const string errorMessage = "Authentication token is missing or expired.";
        var exception = new UnauthorizedException(errorCode, errorMessage);

        // Act
        var (context, body) = await ExecuteHandlerAsync(exception);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        JsonElement root = body.RootElement;
        Assert.Equal(401, root.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, root.GetProperty("code").GetString());
        Assert.Equal(errorMessage, root.GetProperty("detail").GetString());
        Assert.Equal("Unauthorized", root.GetProperty("title").GetString());
        Assert.Equal("https://errors.smartlocker.vn/auth/unauthorized", root.GetProperty("type").GetString());
    }

    [Fact]
    public void ProblemResponse_Create_BuildsPureProblemDetailsCorrectly()
    {
        // Arrange
        DefaultHttpContext httpContext = new() { TraceIdentifier = "trace-pure-unit-test-123" };
        const int status = StatusCodes.Status404NotFound;
        const string code = "domain/item_not_found";
        const string detail = "Requested domain entity was missing.";

        // Act
        ProblemDetails problem = ProblemResponse.Create(httpContext, status, code, detail);

        // Assert
        Assert.Equal(404, problem.Status);
        Assert.Equal(detail, problem.Detail);
        Assert.Equal("Not Found", problem.Title);
        Assert.Equal("https://errors.smartlocker.vn/domain/item-not-found", problem.Type);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal("trace-pure-unit-test-123", problem.Extensions["traceId"]);
    }

    [Fact]
    public void ProblemResponse_Create_WithValidationErrors_BuildsValidationProblemDetails()
    {
        // Arrange
        DefaultHttpContext httpContext = new() { TraceIdentifier = "trace-validation-test-456" };
        const int status = StatusCodes.Status400BadRequest;
        const string code = CommonErrorCodes.InvalidInput;
        const string detail = "One or more validation errors occurred.";
        var errors = new Dictionary<string, string[]>
        {
            ["name"] = ["Name is required."]
        };

        // Act
        ProblemDetails problem = ProblemResponse.Create(httpContext, status, code, detail, errors);

        // Assert
        ValidationProblemDetails validationProblem = Assert.IsType<ValidationProblemDetails>(problem);
        Assert.Equal(400, validationProblem.Status);
        Assert.Equal("One or more validation errors occurred.", validationProblem.Title);
        Assert.Equal(code, validationProblem.Extensions["code"]);
        Assert.Equal("trace-validation-test-456", validationProblem.Extensions["traceId"]);
        Assert.True(validationProblem.Errors.ContainsKey("name"));
        Assert.Equal("Name is required.", Assert.Single(validationProblem.Errors["name"]));
    }
}
