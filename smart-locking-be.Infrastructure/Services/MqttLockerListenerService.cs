using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text.Json;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class MqttLockerListenerService(
    IConfiguration configuration,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MqttLockerListenerService> logger) : BackgroundService
{
    private IMqttClient? _mqttClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string host = configuration["Mqtt:Host"]?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(host))
        {
            logger.LogWarning("[MQTT LISTENER] 'Mqtt:Host' is not configured. MQTT listener will not start.");
            return;
        }

        int port = int.TryParse(configuration["Mqtt:Port"], out int p) ? p : 8883;
        string? username = configuration["Mqtt:Username"];
        string? password = configuration["Mqtt:Password"];
        bool useTls = !bool.TryParse(configuration["Mqtt:UseTls"], out bool configuredTls) || configuredTls;

        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"boxora-listener-{Guid.NewGuid():N}"[..22])
            .WithTcpServer(host, port);

        if (!string.IsNullOrWhiteSpace(username))
        {
            optionsBuilder.WithCredentials(username, password);
        }

        if (useTls)
        {
            optionsBuilder.WithTlsOptions(options =>
            {
                options.UseTls();
            });
        }

        var options = optionsBuilder.Build();

        _mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = e.ApplicationMessage.ConvertPayloadToString();
                await HandleMessageAsync(topic, payload, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[MQTT LISTENER] Failed to process message from topic '{Topic}': {Message}",
                    e.ApplicationMessage.Topic, ex.Message);
            }
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    logger.LogInformation("[MQTT LISTENER] Connecting to EMQX broker ({Host}:{Port})...", host, port);
                    await _mqttClient.ConnectAsync(options, stoppingToken);
                    logger.LogInformation("[MQTT LISTENER] Connected. Subscribing to 'lockers/+/doors/+/status'...");

                    await _mqttClient.SubscribeAsync("lockers/+/doors/+/status", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/alerts", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/heartbeat", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/status", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    logger.LogInformation("[MQTT LISTENER] Subscribed to door, alert, heartbeat and connection status topics.");
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("[MQTT LISTENER] EMQX connection failed: {Message}. Retrying in 5 seconds...", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task HandleMessageAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        // Topic pattern: lockers/{deviceId}/doors/{channel}/status
        var parts = topic.Split('/');
        if (parts.Length == 3 && parts[0] == "lockers" && parts[2] == "alerts")
        {
            await RecordDeviceAlertAsync(parts[1], payload, cancellationToken);
            return;
        }
        if (parts.Length == 3 && parts[0] == "lockers" && !string.IsNullOrWhiteSpace(parts[1]))
        {
            LockerConnectionStatus? connection = parts[2] switch
            {
                "heartbeat" => LockerConnectionStatus.Online,
                "status" when payload.Trim().Equals("online", StringComparison.OrdinalIgnoreCase) => LockerConnectionStatus.Online,
                "status" when payload.Trim().Equals("offline", StringComparison.OrdinalIgnoreCase) => LockerConnectionStatus.Offline,
                _ => null
            };
            if (connection.HasValue)
            {
                using var signalScope = serviceScopeFactory.CreateScope();
                await signalScope.ServiceProvider.GetRequiredService<LockerConnectionMonitor>()
                    .RecordSignalAsync(parts[1], connection.Value, cancellationToken);
            }
            return;
        }

        if (parts.Length != 5 || parts[0] != "lockers" || parts[2] != "doors" || parts[4] != "status")
        {
            return;
        }

        string deviceIdentifier = parts[1];
        if (!int.TryParse(parts[3], out int channel))
        {
            return;
        }

        string statusStr = payload.Trim().ToUpperInvariant();
        DateTimeOffset? deviceTime = null;
        if (payload.TrimStart().StartsWith('{'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                statusStr = document.RootElement.GetProperty("status").GetString()?.Trim().ToUpperInvariant() ?? string.Empty;
                if (document.RootElement.TryGetProperty("occurredAt", out JsonElement timestamp) &&
                    timestamp.TryGetDateTimeOffset(out DateTimeOffset parsed)) deviceTime = parsed;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                logger.LogWarning("[MQTT LISTENER] Ignoring invalid door payload on {Topic}.", topic);
                return;
            }
        }
        if (statusStr is not ("OPEN" or "CLOSED"))
        {
            logger.LogWarning("[MQTT LISTENER] Ignoring invalid door status: '{Payload}'.", payload);
            return;
        }
        DoorStatus doorStatus = statusStr == "CLOSED" ? DoorStatus.Closed : DoorStatus.Open;

        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var locker = await dbContext.Lockers
            .Include(l => l.Compartments)
            .FirstOrDefaultAsync(l => l.DeviceIdentifier == deviceIdentifier, cancellationToken);

        if (locker is null)
        {
            logger.LogDebug("[MQTT LISTENER] Received a message from unregistered device '{DeviceId}'.", deviceIdentifier);
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset occurredAt = deviceTime ?? now;
        if (occurredAt > now.AddMinutes(5)) return;
        var compartment = locker.Compartments.FirstOrDefault(c => c.HardwareChannel == channel);
        if (compartment is null) return;
        if (deviceTime.HasValue && compartment.DoorStatusObservedAt >= occurredAt) return;

        await scope.ServiceProvider.GetRequiredService<LockerConnectionMonitor>()
            .RecordSignalAsync(deviceIdentifier, LockerConnectionStatus.Online, cancellationToken);

        DoorStatus previousDoorStatus = compartment.DoorStatus;
        compartment.DoorStatusObservedAt = occurredAt;
        if (previousDoorStatus != doorStatus)
        {
            compartment.DoorStatus = doorStatus;
            compartment.UpdatedAt = now;
            dbContext.LockerEvents.Add(new LockerEvent
            {
                Id = Guid.NewGuid(),
                LockerId = locker.Id,
                LockerCompartmentId = compartment.Id,
                EventType = LockerEventType.DoorChanged,
                PreviousValue = previousDoorStatus.ToString(),
                NewValue = doorStatus.ToString(),
                Severity = LockerEventSeverity.Info,
                OccurredAt = occurredAt,
                ReceivedAt = now
            });
        }

        if (previousDoorStatus == DoorStatus.Open && doorStatus == DoorStatus.Closed)
        {
            await FinalizeActiveAccessAsync(scope.ServiceProvider, dbContext, compartment.Id, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (previousDoorStatus != doorStatus)
        {
            await scope.ServiceProvider.GetRequiredService<IOperationsRealtimeNotifier>()
                .PublishToLockerAsync(locker.Id, new RealtimeEvent("LockerDoorChanged", compartment.Id,
                    locker.Id, doorStatus.ToString(), compartment.Code, now), cancellationToken);
        }

        logger.LogInformation("[MQTT IN] Locker '{DeviceId}' - compartment {Channel} -> door: {Status} (online)",
            deviceIdentifier, channel, doorStatus);
    }

    private async Task RecordDeviceAlertAsync(string deviceIdentifier, string payload, CancellationToken cancellationToken)
    {
        string alert = payload.Trim();
        if (alert.Length is < 1 or > 500) return;
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Locker? locker = await dbContext.Lockers.SingleOrDefaultAsync(item => item.DeviceIdentifier == deviceIdentifier, cancellationToken);
        if (locker is null) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        // ponytail: suppress repeated MQTT alerts for 10 seconds; use device event IDs if firmware supplies them.
        if (await dbContext.LockerEvents.AnyAsync(item => item.LockerId == locker.Id &&
            item.EventType == LockerEventType.DeviceAlert && item.Reason == alert &&
            item.ReceivedAt >= now.AddSeconds(-10), cancellationToken)) return;
        dbContext.LockerEvents.Add(new LockerEvent
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            EventType = LockerEventType.DeviceAlert,
            Severity = LockerEventSeverity.Warning,
            Reason = alert,
            OccurredAt = now,
            ReceivedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IOperationsRealtimeNotifier>()
            .PublishToLockerAsync(locker.Id, new RealtimeEvent("LockerDeviceAlert", locker.Id,
                locker.Id, "Warning", alert, now), cancellationToken);
    }

    private static async Task FinalizeActiveAccessAsync(
        IServiceProvider services,
        ApplicationDbContext dbContext,
        Guid compartmentId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        LockerAccessEvent? access = await dbContext.LockerAccessEvents
            .AsNoTracking()
            .Where(item => item.LockerCompartmentId == compartmentId && item.Result == LockerAccessResult.Succeeded)
            .OrderByDescending(item => item.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (access is null) return;

        switch (access.AccessType)
        {
            case LockerAccessType.ShipperDropOff when access.DeliveryRequestId.HasValue:
                await services.GetRequiredService<IDeliveryRequestService>()
                    .FinalizeDropOffAsync(access.DeliveryRequestId.Value, completedAt, cancellationToken);
                break;
            case LockerAccessType.ResidentPickup when access.ParcelId.HasValue:
                await services.GetRequiredService<IParcelService>()
                    .FinalizeRetrievalAsync(access.ParcelId.Value, access.UserId, completedAt, cancellationToken);
                break;
            case LockerAccessType.ResidentReturnDropOff when access.ReturnRequestId.HasValue:
                await services.GetRequiredService<IReturnRequestService>()
                    .FinalizeDepositAsync(access.ReturnRequestId.Value, completedAt, cancellationToken);
                break;
            case LockerAccessType.ShipperReturnPickup when access.ReturnRequestId.HasValue:
                await services.GetRequiredService<IReturnPickupSessionService>()
                    .FinalizePickupAsync(access.ReturnRequestId.Value, completedAt, cancellationToken);
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_mqttClient is not null && _mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync(cancellationToken: cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}
