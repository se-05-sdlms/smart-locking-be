using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
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
            logger.LogWarning("[MQTT LISTENER] Chưa cấu hình 'Mqtt:Host'. Dịch vụ lắng nghe MQTT sẽ không khởi động.");
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
                options.WithCertificateValidationHandler(_ => true);
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
                logger.LogError(ex, "[MQTT LISTENER] Lỗi xử lý bản tin từ topic '{Topic}': {Message}",
                    e.ApplicationMessage.Topic, ex.Message);
            }
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    logger.LogInformation("[MQTT LISTENER] Đang kết nối tới EMQX Broker ({Host}:{Port})...", host, port);
                    await _mqttClient.ConnectAsync(options, stoppingToken);
                    logger.LogInformation("[MQTT LISTENER] Đã kết nối thành công! Đăng ký lắng nghe 'lockers/+/doors/+/status'...");

                    await _mqttClient.SubscribeAsync("lockers/+/doors/+/status", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                    logger.LogInformation("[MQTT LISTENER] Đã subscribe thành công topic trạng thái cửa.");
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("[MQTT LISTENER] Kết nối tới EMQX thất bại: {Message}. Thử lại sau 5 giây...", ex.Message);
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
        if (statusStr is not ("OPEN" or "CLOSED"))
        {
            logger.LogWarning("[MQTT LISTENER] Bỏ qua trạng thái cửa không hợp lệ: '{Payload}'.", payload);
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
            logger.LogDebug("[MQTT LISTENER] Nhận bản tin từ thiết bị '{DeviceId}' nhưng chưa được đăng ký trong hệ thống.", deviceIdentifier);
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        locker.ConnectionStatus = LockerConnectionStatus.Online;
        locker.LastSeenAt = now;
        locker.UpdatedAt = now;

        var compartment = locker.Compartments.FirstOrDefault(c => c.HardwareChannel == channel);
        DoorStatus? previousDoorStatus = compartment?.DoorStatus;
        if (compartment is not null)
        {
            compartment.DoorStatus = doorStatus;
            compartment.UpdatedAt = now;
        }

        if (compartment is not null && previousDoorStatus == DoorStatus.Open && doorStatus == DoorStatus.Closed)
        {
            await FinalizeActiveAccessAsync(scope.ServiceProvider, dbContext, compartment.Id, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[MQTT IN] Locker '{DeviceId}' - Ngăn {Channel} -> Cửa: {Status} (Online)",
            deviceIdentifier, channel, doorStatus);
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
