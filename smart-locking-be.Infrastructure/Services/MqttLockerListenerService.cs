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
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class MqttLockerListenerService(
    IConfiguration configuration,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MqttLockerListenerService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
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
                await HandleMessageAsync(topic, payload, stoppingToken, e.ApplicationMessage.Retain);
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
                    await _mqttClient.SubscribeAsync("lockers/+/events/pin-ready", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/events/offline", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/events/state-reset", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/alerts", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/heartbeat", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    await _mqttClient.SubscribeAsync("lockers/+/status", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    logger.LogInformation("[MQTT LISTENER] Subscribed to door, alert, heartbeat and connection status topics.");
                }
                await PublishInventoryAsync(stoppingToken);
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

    private async Task PublishInventoryAsync(CancellationToken ct)
    {
        if (_mqttClient is not { IsConnected: true }) return;
        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await scope.ServiceProvider.GetRequiredService<OfflinePinProvisioner>().EnsureAsync(ct);
        DateTimeOffset now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        bool bluetoothEnabled = await db.SystemPolicies.AnyAsync(x => x.IsActive && x.EnableBluetoothUnlock && x.EffectiveFrom <= now &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo > now), ct);
        var channels = await db.LockerCompartments.Select(x => new
        {
            Device = x.Locker.DeviceIdentifier,
            Channel = x.HardwareChannel,
            Busy = x.OperationalStatus != LockerCompartmentOperationalStatus.Operational || x.Locker.OperationalStatus != LockerOperationalStatus.Operational ||
                db.Parcels.Any(p => p.DeliveryRequest.AllocatedCompartmentId == x.Id && (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue)) ||
                db.ReturnRequests.Any(r => r.AllocatedCompartmentId == x.Id && (r.Status == ReturnRequestStatus.Allocated || r.Status == ReturnRequestStatus.Deposited)) ||
                db.CompartmentReservations.Any(r => r.LockerCompartmentId == x.Id && r.ReleasedAt == null && r.ExpiresAt > now)
        }).ToListAsync(ct);
        foreach (var locker in channels.GroupBy(x => x.Device))
        {
            int mask = locker.Where(x => x.Channel is >= 1 and <= 4 && x.Busy).Aggregate(0, (value, x) => value | (1 << (x.Channel - 1)));
            // Ngăn không tồn tại trên server cũng không được coi là ngăn trống.
            int known = locker.Where(x => x.Channel is >= 1 and <= 4).Aggregate(0, (value, x) => value | (1 << (x.Channel - 1)));
            mask |= 15 & ~known;
            await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"lockers/{locker.Key}/inventory")
                .WithPayload($"{(bluetoothEnabled ? 1 : 0)}:{mask}").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), ct);
        }
        var pins = await db.OfflineLockerPins.Where(x => x.ConsumedAt == null &&
            db.Parcels.Any(p => p.Id == x.ParcelId && (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue) &&
                p.DeliveryRequest.AllocatedCompartment!.OperationalStatus == LockerCompartmentOperationalStatus.Operational))
            .Join(db.Parcels, pin => pin.ParcelId, parcel => parcel.Id, (pin, parcel) => new
            {
                Device = parcel.DeliveryRequest.Locker.DeviceIdentifier,
                Channel = parcel.DeliveryRequest.AllocatedCompartment!.HardwareChannel,
                pin.DeliveryId,
                pin.RecipientPhone,
                pin.PinCode
            }).ToListAsync(ct);
        foreach (var pin in pins)
            await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"lockers/{pin.Device}/pins")
                .WithPayload($"{pin.Channel}:{pin.DeliveryId:N}:{pin.RecipientPhone}:{pin.PinCode}")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), ct);
    }

    internal async Task HandleMessageAsync(string topic, string payload, CancellationToken cancellationToken, bool isRetained = false)
    {
        // Giữ luồng MQTT hiện có. DeviceId được đối chiếu với topic, nhưng o1/z2 chưa có
        // chữ ký; xác thực publisher theo thiết bị vẫn cần ACL broker khi triển khai bảo mật.
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

        if (parts.Length == 4 && parts[0] == "lockers" && parts[2] == "events" && parts[3] == "state-reset")
        {
            using var resetScope = serviceScopeFactory.CreateScope();
            await resetScope.ServiceProvider.GetRequiredService<ICompartmentResetService>().ReceiveAsync(parts[1], payload, cancellationToken);
            await PublishInventoryAsync(cancellationToken);
            return;
        }
        if (parts.Length == 4 && parts[0] == "lockers" && parts[2] == "events" && parts[3] == "pin-ready")
        {
            using var pinScope = serviceScopeFactory.CreateScope();
            await pinScope.ServiceProvider.GetRequiredService<IOfflinePinService>().ConfirmProvisionAsync(parts[1], payload, cancellationToken);
            return;
        }
        if (parts.Length == 4 && parts[0] == "lockers" && parts[2] == "events" && parts[3] == "offline")
        {
            // Không xử lý lại JSON/PIN tự khai từ phiên bản demo cũ.
            if (!payload.StartsWith("o1|", StringComparison.Ordinal)) return;
            using var offlineScope = serviceScopeFactory.CreateScope();
            var service = offlineScope.ServiceProvider.GetRequiredService<IOfflinePinService>();
            var result = await service.ReceiveAsync(parts[1], payload, cancellationToken);
            if (_mqttClient is { IsConnected: true })
            {
                var ack = new MqttApplicationMessageBuilder().WithTopic($"lockers/{parts[1]}/events/offline/ack")
                    .WithPayload(result.Acknowledgment).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build();
                await _mqttClient.PublishAsync(ack, cancellationToken);
            }
            return;
        }
        if (parts.Length != 5 || parts[0] != "lockers" || parts[2] != "doors" || parts[4] != "status")
        {
            return;
        }
        if (isRetained) return;

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

        DateTimeOffset now = (timeProvider ?? TimeProvider.System).GetUtcNow();
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
        // Khi dùng lệnh workflow c1 qua MQTT hoặc BLE, chỉ nhật ký o1 của đúng
        // giao dịch mới xác nhận hoàn tất. Cửa đóng không chứng minh lệnh đã đến ESP.
        // Giữ cách xử lý cũ cho hệ thống legacy chưa cấu hình bộ mã hóa workflow.
        if (access.AccessMethod == LockerAccessMethod.Bluetooth ||
            services.GetService<IBluetoothMessageCodec>() is not null) return;

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
