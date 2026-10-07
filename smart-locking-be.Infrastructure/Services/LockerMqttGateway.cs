using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerMqttGateway(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LockerMqttGateway> logger) : BackgroundService, ILockerCommandDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
    private CancellationToken stoppingToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.stoppingToken = stoppingToken;
        string host = configuration["Mqtt:Host"]?.Trim() ?? string.Empty;
        if (host.Length == 0)
        {
            logger.LogWarning("MQTT broker chưa được cấu hình; gateway không hoạt động.");
            return;
        }

        int port = int.TryParse(configuration["Mqtt:Port"], out int configuredPort) ? configuredPort : 8883;
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"boxora-api-{Guid.NewGuid():N}")
            .WithTcpServer(host, port);
        string? username = configuration["Mqtt:Username"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            optionsBuilder.WithCredentials(username, configuration["Mqtt:Password"]);
        }
        if (!bool.TryParse(configuration["Mqtt:UseTls"], out bool useTls) || useTls)
        {
            optionsBuilder.WithTlsOptions(options => options.UseTls());
        }

        var options = optionsBuilder.Build();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DisconnectedAsync += _ =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };
        client.ApplicationMessageReceivedAsync += HandleMessageAsync;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    await client.ConnectAsync(options, stoppingToken);
                    var subscription = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(filter => filter.WithTopic("boxora/lockers/+/events/#")
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                        .Build();
                    var result = await client.SubscribeAsync(subscription, stoppingToken);
                    if (result.Items.Any(item => (int)item.ResultCode >= 128))
                    {
                        throw new InvalidOperationException("MQTT broker từ chối đăng ký sự kiện locker.");
                    }
                    await disconnected.Task.WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Kết nối MQTT gián đoạn; thử lại sau 5 giây.");
                    if (client.IsConnected)
                    {
                        await DisconnectAsync();
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        finally
        {
            client.ApplicationMessageReceivedAsync -= HandleMessageAsync;
            if (client.IsConnected)
            {
                await DisconnectAsync();
            }
        }
    }

    private async Task DisconnectAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.DisconnectAsync(cancellationToken: timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Không thể đóng kết nối MQTT.");
        }
    }

    public async Task DispatchUnlockAsync(LockerUnlockCommand command, CancellationToken cancellationToken = default)
    {
        if (!client.IsConnected)
        {
            throw new InvalidOperationException("MQTT broker chưa kết nối.");
        }
        if (string.IsNullOrWhiteSpace(command.DeviceIdentifier) ||
            command.DeviceIdentifier.IndexOfAny(['/', '+', '#', '\0']) >= 0)
        {
            throw new ArgumentException("Mã thiết bị MQTT không hợp lệ.");
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"boxora/lockers/{command.DeviceIdentifier}/commands/unlock")
            .WithPayload(JsonSerializer.Serialize(new { commandId = command.CommandId, hardwareChannel = command.HardwareChannel }))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        cancellationToken.ThrowIfCancellationRequested();
        MqttClientPublishResult result;
        try
        {
            result = await client.PublishAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Chưa rõ kết quả gửi lệnh {CommandId}; tiếp tục chờ cảm biến.", command.CommandId);
            throw new LockerCommandDeliveryUnknownException(exception);
        }
        if ((int)result.ReasonCode >= 128)
        {
            throw new InvalidOperationException("MQTT broker từ chối lệnh mở ngăn.");
        }
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        string topic = args.ApplicationMessage.Topic;
        try
        {
            string[] parts = topic.Split('/');
            if (parts.Length != 5 || parts[0] != "boxora" || parts[1] != "lockers" ||
                string.IsNullOrWhiteSpace(parts[2]) || parts[3] != "events")
            {
                logger.LogWarning("Bỏ qua MQTT topic không hợp lệ: {Topic}", topic);
                return;
            }

            string payload = args.ApplicationMessage.ConvertPayloadToString();
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ILockerDeviceEventHandler>();
            switch (parts[4])
            {
                case "command-ack":
                    var ack = JsonSerializer.Deserialize<CommandAckPayload>(payload, JsonOptions)
                        ?? throw new JsonException("Thiếu ACK payload.");
                    if (ack.CommandId == Guid.Empty) throw new JsonException("commandId không hợp lệ.");
                    await handler.HandleCommandAckAsync(parts[2], ack.CommandId, ack.Ok, stoppingToken);
                    break;
                case "door":
                    var door = JsonSerializer.Deserialize<DoorEventPayload>(payload, JsonOptions)
                        ?? throw new JsonException("Thiếu door payload.");
                    DoorStatus state = door.State switch
                    {
                        "open" => DoorStatus.Open,
                        "closed" => DoorStatus.Closed,
                        _ => throw new JsonException("Trạng thái cửa không hợp lệ."),
                    };
                    await handler.HandleDoorChangedAsync(parts[2], door.HardwareChannel, state,
                        door.At?.ToUniversalTime() ?? timeProvider.GetUtcNow(), stoppingToken);
                    break;
                case "status":
                    var status = JsonSerializer.Deserialize<StatusPayload>(payload, JsonOptions)
                        ?? throw new JsonException("Thiếu status payload.");
                    await handler.HandleConnectionChangedAsync(parts[2], status.Online, stoppingToken);
                    break;
                default:
                    logger.LogWarning("Bỏ qua MQTT topic chưa hỗ trợ: {Topic}", topic);
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Không thể xử lý MQTT event trên {Topic}.", topic);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        client.Dispose();
    }
}
