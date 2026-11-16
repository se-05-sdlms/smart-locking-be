using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using smart_locking_be.API.Controllers;
using smart_locking_be.API.Extensions;
using smart_locking_be.API.Middleware;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace smart_locking_be.Tests.API;

public sealed class WaybillUploadHttpBoundaryTests
{
    private const int FiveMiB = 5 * 1024 * 1024;

    [Theory]
    [InlineData(FiveMiB, HttpStatusCode.OK)]
    [InlineData(FiveMiB + 1, HttpStatusCode.BadRequest)]
    [InlineData(FiveMiB + 512 * 1024, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task RealMultipart_EnforcesFileBoundaryBeforeStorageOrOcr(int fileBytes, HttpStatusCode expected)
    {
        // Real Kestrel + MVC/form binding, real delivery service and in-memory session persistence.
        // Recording storage/OCR boundaries never use a provider or write files.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        builder.Services.AddScoped(_ => new ApplicationDbContext(options));
        var storage = new RecordingStorage();
        var ocr = new RecordingOcr();
        var tokens = new Sha256TokenHashService();
        builder.Services.AddScoped<IDeliveryRequestService>(sp => new DeliveryRequestService(
            sp.GetRequiredService<ApplicationDbContext>(), tokens, null!, null!, null!, storage, ocr, null!, TimeProvider.System));
        builder.Services.AddControllers().AddApplicationPart(typeof(DeliveryRequestsController).Assembly);
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddFileUploadLimits(builder.Configuration);
        builder.Services.AddApiRateLimiting(builder.Configuration);
        builder.Services.AddApiRequestTimeouts(builder.Configuration);
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseApiRequestTimeouts();
        app.UseAuthorization();
        app.MapControllers();

        Guid requestId = Guid.NewGuid();
        const string token = "test-waybill-guest-session";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var policy = new SystemPolicy { Id = Guid.NewGuid(), GuestSessionTimeoutMinutes = 10 };
            db.Add(new DeliveryRequest
            {
                Id = requestId,
                Locker = new Locker { Id = Guid.NewGuid(), Code = "WAYBILL", DeviceIdentifier = "WAYBILL" },
                SystemPolicy = policy,
                GuestSessionTokenHash = tokens.HashToken(token),
                Status = DeliveryRequestStatus.Started,
                SessionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Add("X-Guest-Session-Token", token);
        using var multipart = new MultipartFormDataContent("waybill-boundary-test");
        using var image = new ByteArrayContent(new byte[fileBytes]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(image, "file", "waybill.jpg");
        long requestBytes = multipart.Headers.ContentLength!.Value;
        Assert.True(requestBytes > fileBytes); // The transport limit must leave room for multipart headers/boundaries.
        using var response = await client.PostAsync($"/api/delivery-requests/{requestId}:waybill-ocr", multipart);
        Assert.Equal(expected, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await using var readScope = app.Services.CreateAsyncScope();
        var read = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await read.DeliveryRequests.SingleAsync();
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(1, storage.Calls);
            Assert.Equal(fileBytes, storage.Bytes);
            Assert.Equal(1, ocr.Calls);
            Assert.Equal(fileBytes, ocr.Bytes);
            Assert.Equal("https://example.test/waybill.jpg", persisted.ParcelImageUrl);
            Assert.Equal(OcrStatus.Succeeded, persisted.OcrStatus);
            Assert.Single(await read.AuditLogs.Where(a => a.Action == "DeliveryRequest.WaybillScanned").ToListAsync());
        }
        else
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ImageErrorCodes.InvalidImage, body.RootElement.GetProperty("code").GetString());
            Assert.Equal(fileBytes == 0 ? "Image must not be empty." : "Image must not exceed 5 MB.",
                body.RootElement.GetProperty("detail").GetString());
            Assert.Equal(0, storage.Calls);
            Assert.Equal(0, ocr.Calls);
            Assert.Null(persisted.ParcelImageUrl);
            Assert.Null(persisted.OcrStatus);
            Assert.Empty(await read.AuditLogs.ToListAsync());
        }
    }

    private sealed class RecordingStorage : IImageStorageService
    {
        public int Calls { get; private set; }
        public long Bytes { get; private set; }
        public async Task<string> UploadAsync(Stream image, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            Calls++;
            using var buffer = new MemoryStream();
            await image.CopyToAsync(buffer, cancellationToken);
            Bytes = buffer.Length;
            return "https://example.test/waybill.jpg";
        }
    }

    private sealed class RecordingOcr : IWaybillOcrService
    {
        public int Calls { get; private set; }
        public int Bytes { get; private set; }
        public Task<WaybillOcrResult> ExtractRecipientPhonesAsync(byte[] image, string contentType, CancellationToken cancellationToken = default)
        {
            Calls++;
            Bytes = image.Length;
            return Task.FromResult(new WaybillOcrResult(true, ["0901234001"], false));
        }
    }
}
