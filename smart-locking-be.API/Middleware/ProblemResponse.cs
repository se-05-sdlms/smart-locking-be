using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;

namespace smart_locking_be.API.Middleware;

public static class ProblemResponse
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return JsonSerializer.SerializeAsync(context.Response.Body, Create(context, status, code, detail),
            new JsonSerializerOptions(JsonSerializerDefaults.Web), CancellationToken.None);
    }

    public static ProblemDetails Create(
        HttpContext context,
        int status,
        string code,
        string detail,
        IDictionary<string, string[]>? errors = null)
    {
        ProblemDetails problem = errors is null ? new ProblemDetails() : new ValidationProblemDetails(errors);
        problem.Type = $"https://errors.smartlocker.vn/{code.Replace('_', '-')}";
        problem.Title = errors is null ? ReasonPhrases.GetReasonPhrase(status) : "One or more validation errors occurred.";
        problem.Status = status;
        problem.Detail = detail;
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }
}
