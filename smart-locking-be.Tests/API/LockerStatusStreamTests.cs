using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Controllers;
using smart_locking_be.API.Services;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.API;

public sealed class LockerStatusStreamTests
{
    [Fact]
    public async Task AudienceLookupFailure_DoesNotUndoSseSignalOrFailBusinessOperation()
    {
        var feed = new LockerStatusStream();
        using var subscription = feed.Subscribe("ESP-B");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var signal = subscription.WaitAsync(timeout.Token).AsTask();
        var notifier = new SignalROperationsRealtimeNotifier(null!, NullLogger<SignalROperationsRealtimeNotifier>.Instance,
            new FailingAudience(), TimeProvider.System, feed);
        Guid lockerId = Guid.NewGuid();
        await notifier.PublishToLockerAsync(lockerId, new("LockerDoorChanged", null, lockerId, "Open", null, DateTimeOffset.UtcNow));
        Assert.True(await signal);
    }

    private sealed class FailingAudience : IOperationsRealtimeAudience
    {
        public Task<string?> GetDeviceIdentifierAsync(Guid lockerId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("ESP-B");
        public Task<IReadOnlyCollection<Guid>> GetRecipientIdsAsync(Guid lockerId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Recipient lookup unavailable");
    }

    [Fact]
    public async Task MqttDoorUpdate_SignalsStreamAfterPersistingSensorState()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSignalR();
        string database = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<LockerStatusStream>();
        services.AddScoped<IOperationsRealtimeNotifier, SignalROperationsRealtimeNotifier>();
        services.AddScoped<IOperationsRealtimeAudience, OperationsRealtimeAudience>();
        services.AddScoped<LockerConnectionMonitor>();
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "ESP-B", ConnectionStatus = LockerConnectionStatus.Online };
            db.Add(new LockerCompartment { Id = Guid.NewGuid(), Locker = locker, LockerId = locker.Id, HardwareChannel = 1, DoorStatus = DoorStatus.Closed });
            await db.SaveChangesAsync();
        }
        var feed = provider.GetRequiredService<LockerStatusStream>();
        using var subscription = feed.Subscribe("ESP-B");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var signal = subscription.WaitAsync(timeout.Token).AsTask();
        var listener = new MqttLockerListenerService(new ConfigurationBuilder().Build(),
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MqttLockerListenerService>.Instance);
        await listener.HandleMessageAsync("lockers/ESP-B/doors/1/status", "OPEN", timeout.Token);
        Assert.True(await signal);
        using var read = provider.CreateScope();
        var slot = await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().LockerCompartments.SingleAsync();
        Assert.Equal(DoorStatus.Open, slot.DoorStatus); Assert.NotNull(slot.DoorStatusObservedAt);
    }

    [Fact]
    public async Task Signal_ReachesEverySubscriberOfCorrectDevice_AndCleansUp()
    {
        var stream = new LockerStatusStream();
        using var a = stream.Subscribe("A"); using var b = stream.Subscribe("A");
        using var other = stream.Subscribe("B");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var first = a.WaitAsync(timeout.Token).AsTask(); var second = b.WaitAsync(timeout.Token).AsTask();
        var unrelated = other.WaitAsync(timeout.Token).AsTask();
        stream.Publish("A");
        Assert.True(await first); Assert.True(await second); Assert.False(unrelated.IsCompleted);
        timeout.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unrelated);
        a.Dispose(); b.Dispose(); other.Dispose();
        Assert.False(stream.HasSubscribers);
    }

    [Fact]
    public async Task Sse_FlushesInitialSnapshot_ThenPushesChangedStateWithoutPollingDelay()
    {
        var feed = new LockerStatusStream();
        var service = DispatchProxy.Create<ILockerService, StatusProxy>();
        var proxy = (StatusProxy)(object)service;
        await using var app = WebApplication.CreateBuilder().Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapGet("/status", async (HttpContext context) =>
        {
            var controller = new LockersController(service) { ControllerContext = new ControllerContext { HttpContext = context } };
            await controller.StreamPublicStatus("B", feed, context.RequestAborted);
        });
        await app.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new HttpClient();
        using var response = await client.GetAsync(app.Urls.Single() + "/status", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        async Task<string> ReadDoor()
        {
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                    return JsonDocument.Parse(line[6..]).RootElement.GetProperty("compartments")[0].GetProperty("doorStatus").GetString()!;
            throw new Exception("SSE closed before snapshot.");
        }
        Assert.Equal("Closed", await ReadDoor());
        proxy.Door = "Open";
        feed.Publish("ESP-B");
        Assert.Equal("Open", await ReadDoor().WaitAsync(TimeSpan.FromSeconds(1)));
        timeout.Cancel();
        response.Dispose();
        await app.StopAsync();
    }

    public class StatusProxy : DispatchProxy
    {
        public string Door { get; set; } = "Closed";
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(ILockerService.GetPublicLockerStatusAsync)
                ? Task.FromResult(new PublicLockerStatusResponse("B", "ESP-B", "Online", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    [new(1, Door, DateTimeOffset.UtcNow, "EMPTY")]))
                : throw new NotSupportedException();
    }
}
