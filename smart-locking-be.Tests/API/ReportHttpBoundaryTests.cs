using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smart_locking_be.API.Controllers;
using smart_locking_be.Application.Interfaces.Services;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace smart_locking_be.Tests.API;

public sealed class ReportHttpBoundaryTests
{
    [Theory]
    [InlineData("summary", "2026-11-16", "2026-11-17", "2026-11-15T17:00:00Z")]
    [InlineData("operational-activities", "2026-11-16", "2026-11-17", "2026-11-15T17:00:00Z")]
    [InlineData("operational-activities/export", "2026-11-16", "2026-11-17", "2026-11-15T17:00:00Z")]
    [InlineData("summary", "2026-11-16T03:00:00+07:00", "2026-11-17T03:00:00+07:00", "2026-11-15T20:00:00Z")]
    [InlineData("operational-activities", "2026-11-16T03:00:00+07:00", "2026-11-17T03:00:00+07:00", "2026-11-15T20:00:00Z")]
    [InlineData("operational-activities/export", "2026-11-16T03:00:00+07:00", "2026-11-17T03:00:00+07:00", "2026-11-15T20:00:00Z")]
    public async Task RealMvcModelBinding_AppliesSameUtcBounds(string endpoint, string from, string to, string expectedStart)
    {
        // Test-only principal isolates date binding from the separately tested login flow.
        // No production host/worker/provider or application database is started.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var capture = new ReportBoundaryTests.CapturingService();
        builder.Services.AddSingleton<IReportService>(capture);
        builder.Services.AddControllers().AddApplicationPart(typeof(ReportsController).Assembly);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAdminHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await client.GetAsync($"/api/reports/{endpoint}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DateTimeOffset.Parse(expectedStart), capture.From);
        Assert.Equal(capture.From.AddDays(1), capture.To);
        Assert.Equal(TimeSpan.Zero, capture.From.Offset);
        Assert.Equal(TimeSpan.Zero, capture.To.Offset);
    }

    private sealed class TestAdminHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, "Administrator")], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }
}
