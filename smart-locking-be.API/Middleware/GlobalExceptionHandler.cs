using Microsoft.AspNetCore.Diagnostics;

namespace smart_locking_be.API.Middleware;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        int statusCode = exception switch
        {
            BadRequestException => StatusCodes.Status400BadRequest,
            UnauthorizedException => StatusCodes.Status401Unauthorized,
            ForbiddenException => StatusCodes.Status403Forbidden,
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            GoneException => StatusCodes.Status410Gone,
            ServiceUnavailableException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "API request failed with status code {StatusCode}", statusCode);

        string detail = exception switch
        {
            ServiceUnavailableException => exception.Message,
            ApiException when statusCode < StatusCodes.Status500InternalServerError => exception.Message,
            _ => "An unexpected error occurred."
        };

        if (statusCode == StatusCodes.Status401Unauthorized)
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";

        httpContext.Response.StatusCode = statusCode;
        string code = exception is ApiException apiException
            ? apiException.ErrorCode
            : CommonErrorCodes.InternalError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = ProblemResponse.Create(httpContext, statusCode, code, detail,
                (exception as BadRequestException)?.Errors),
            Exception = exception
        });
    }
}
