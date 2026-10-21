using System.Security.Claims;

namespace smart_locking_be.API.Middleware;

public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    private static readonly PathString[] AllowedPaths =
    [
        new("/api/auth/me"),
        new("/api/auth/logout"),
        new("/api/auth/refresh-token"),
        new("/api/auth/change-password")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        bool passwordChangeRequired = context.User.Identity?.IsAuthenticated == true &&
            string.Equals(context.User.FindFirstValue("must_change_password"), "true", StringComparison.OrdinalIgnoreCase);
        if (passwordChangeRequired && !AllowedPaths.Any(path => context.Request.Path.Equals(path)))
        {
            await ProblemResponse.WriteAsync(context, StatusCodes.Status403Forbidden,
                AuthErrorCodes.PasswordChangeRequired,
                "You must change the temporary password before continuing.");
            return;
        }

        await next(context);
    }
}
