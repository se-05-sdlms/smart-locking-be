using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using smart_locking_be.API.Controllers;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace smart_locking_be.Tests.Policies;

public sealed class SystemPolicySlaHttpTests
{
    private static readonly Guid AdminId = Guid.Parse("b03f2ec7-b2a7-4a95-a727-a853df2152c9");

    [PostgresFact]
    public async Task RealHttpAndPostgres_FeeOnlyOmittedOrNullSlaPreservesAllTargets()
    {
        string databaseName = $"smart_locking_sla_test_{Guid.NewGuid():N}";
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES")) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            connection.Database = databaseName;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection.ConnectionString));
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddScoped<ISystemPolicyService, SystemPolicyService>();
            builder.Services.AddControllers().AddApplicationPart(typeof(SystemPoliciesController).Assembly);
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAdminHandler>("test", _ => { });
            builder.Services.AddAuthorization();
            await using var app = builder.Build();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.MigrateAsync();
                db.Add(new User { Id = AdminId, Role = UserRole.Administrator, Status = UserStatus.Active, Email = "sla-admin@example.test", PasswordHash = "unused-test-principal" });
                await db.SaveChangesAsync();
            }
            // Test-only authentication; production JWT/login and external providers are not started.
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var custom = new IncidentSlaPolicy(7, 17, 27, 37, 47, 57, 67, 77);
            var request = new SaveSystemPolicyRequest(DeliveryApprovalMode.Auto, 15, 30, 10, 168, 0m, "VND", 336, 336, 24, 5,
                5, 15, true, true, false, DateTimeOffset.UtcNow, IncidentSla: custom);
            using var created = await client.PostAsJsonAsync("/api/system-policies", request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var policy = (await created.Content.ReadFromJsonAsync<SystemPolicyResponse>())!;
            foreach (bool omit in new[] { true, false })
            {
                var feeOnly = JsonSerializer.SerializeToNode(request with { OverdueFeePerHour = omit ? 123m : 456m, IncidentSla = null }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
                if (omit) feeOnly.Remove("incidentSla");
                using var updated = await client.PatchAsJsonAsync($"/api/system-policies/{policy.Id}", feeOnly);
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
                var reloaded = await client.GetFromJsonAsync<SystemPolicyResponse>($"/api/system-policies/{policy.Id}");
                Assert.Equal(custom, reloaded!.IncidentSla);
                Assert.Equal(omit ? 123m : 456m, reloaded.OverdueFeePerHour);
            }
            using var defaultCreated = await client.PostAsJsonAsync("/api/system-policies", request with { IncidentSla = null });
            Assert.Equal(HttpStatusCode.Created, defaultCreated.StatusCode);
            var defaults = (await defaultCreated.Content.ReadFromJsonAsync<SystemPolicyResponse>())!;
            Assert.Equal(new IncidentSlaPolicy(), defaults.IncidentSla);
            Assert.False(defaults.IsActive);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class TestAdminHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()), new Claim(ClaimTypes.Role, "Administrator")], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }
}
