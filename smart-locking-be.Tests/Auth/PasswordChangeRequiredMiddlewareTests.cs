using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using smart_locking_be.API.Middleware;

namespace smart_locking_be.Tests.Auth;

public sealed class PasswordChangeRequiredMiddlewareTests
{
    [Theory]
    [InlineData("/api/returns", true, 403)]
    [InlineData("/api/auth/change-password", true, 204)]
    [InlineData("/api/returns", false, 204)]
    public async Task Temporary_password_only_allows_recovery_endpoints(string path, bool mustChange, int expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("must_change_password", mustChange ? "true" : "false")], "test"));
        var middleware = new PasswordChangeRequiredMiddleware(http => { http.Response.StatusCode = 204; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
    }
}
