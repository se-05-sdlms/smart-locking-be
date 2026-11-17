using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using smart_locking_be.API.Controllers;
using smart_locking_be.API.Extensions;
using smart_locking_be.API.Middleware;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Returns;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace smart_locking_be.Tests.API;

// Real MVC upload/create/read boundaries and migrated disposable PostgreSQL.
// Only the provider transport and authenticated identity are replaced.
public sealed class ReturnImageUrlHttpPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_return_image_test_{Guid.NewGuid():N}";
    private readonly Guid userId = Guid.NewGuid();
    private string connectionString = string.Empty, adminConnectionString = string.Empty;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=");

    public async Task InitializeAsync()
    {
        string? configured = Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        adminConnectionString = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
        await using var db = Context();
        await db.Database.MigrateAsync();
        db.Add(new ResidentProfile
        {
            Id = Guid.NewGuid(),
            FullName = "Image round trip resident",
            User = new User { Id = userId, Email = "return-image@example.test", Role = UserRole.Resident, Status = UserStatus.Active },
            RegisteredLocker = new Locker { Id = Guid.NewGuid(), Code = "RETURN-IMAGE", DeviceIdentifier = "return-image", OperationalStatus = LockerOperationalStatus.Operational }
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(adminConnectionString)) return;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PickupPostgresFact]
    public async Task AbsoluteProviderUrl_IsPreservedThroughBothUploadAliasesAndReturnRoundTrip()
    {
        var provider = new RecordingProvider();
        using var providerClient = new HttpClient(provider);
        var storage = new SupabaseImageStorageService(providerClient, Options.Create(new SupabaseStorageOptions
        {
            Url = "https://storage.example.test/",
            ApiKey = "test-only-key",
            BucketName = "return-images",
            Folder = "/resident returns/proofs/"
        }));
        await using var app = await StartAsync(storage);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        foreach (string endpoint in new[] { "/api/images", "/api/uploads/images" })
        {
            string url = await UploadAsync(client, endpoint);
            Assert.Equal($"https://storage.example.test/storage/v1/object/public/{provider.ObjectPath}", url);
            Assert.Equal(Png, provider.Bytes);
            await AssertReturnRoundTripAsync(client, url);
        }
        Assert.Equal(2, provider.Calls);
    }

    [PickupPostgresFact]
    public async Task RootRelativeStoragePath_AddsRequestOriginExactlyOnceBeforeReturnRoundTrip()
    {
        // Compatibility path: production Supabase returns absolute URLs, older storage may return root-relative paths.
        await using var app = await StartAsync(new RelativeStorage());
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        string url = await UploadAsync(client, "/api/images");
        Assert.Equal($"{client.BaseAddress!.GetLeftPart(UriPartial.Authority)}/uploads/images/return.png", url);
        await AssertReturnRoundTripAsync(client, url);
    }

    private async Task AssertReturnRoundTripAsync(HttpClient client, string url)
    {
        using var response = await client.PostAsJsonAsync("/api/returns", new { imageUrl = url, note = "Hộp sách" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(url, created.RootElement.GetProperty("imageUrl").GetString());
        Guid id = created.RootElement.GetProperty("id").GetGuid();
        using var detailResponse = await client.GetAsync($"/api/returns/{id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(url, detail.RootElement.GetProperty("imageUrl").GetString());
        using var listResponse = await client.GetAsync("/api/returns");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(url, list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id).GetProperty("imageUrl").GetString());
        await using var read = Context();
        Assert.Equal(url, (await read.ReturnRequests.SingleAsync(item => item.Id == id)).ReturnImageUrl);
    }

    private static async Task<string> UploadAsync(HttpClient client, string endpoint)
    {
        using var multipart = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(file, "file", "return.png");
        using var response = await client.PostAsync(endpoint, multipart);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("url").GetString()!;
    }

    private async Task<WebApplication> StartAsync(IImageStorageService storage)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddScoped(_ => Context());
        builder.Services.AddSingleton(storage);
        builder.Services.AddScoped<IReturnRequestService>(sp => new ReturnRequestService(sp.GetRequiredService<ApplicationDbContext>(), null!, null!, null!, new Sha256TokenHashService(), TimeProvider.System));
        builder.Services.AddControllers().AddApplicationPart(typeof(ImagesController).Assembly);
        builder.Services.AddSingleton(new ResidentIdentity(userId));
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, ResidentHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddFileUploadLimits(builder.Configuration);
        builder.Services.AddApiRateLimiting(builder.Configuration);
        builder.Services.AddApiRequestTimeouts(builder.Configuration);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseApiRequestTimeouts();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    private sealed record ResidentIdentity(Guid UserId);

    private sealed class ResidentHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ResidentIdentity resident)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.Role, "Resident"), new Claim(ClaimTypes.NameIdentifier, resident.UserId.ToString())
            ], "test")), "test")));
    }

    private sealed class RecordingProvider : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string ObjectPath { get; private set; } = string.Empty;
        public byte[] Bytes { get; private set; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("storage.example.test", request.RequestUri!.Host);
            ObjectPath = request.RequestUri.AbsolutePath["/storage/v1/object/".Length..];
            Assert.StartsWith("return-images/resident%20returns/proofs/", ObjectPath);
            Bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { Key = ObjectPath }) };
        }
    }

    private sealed class RelativeStorage : IImageStorageService
    {
        public Task<string> UploadAsync(Stream image, string fileName, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult("/uploads/images/return.png");
    }
}
