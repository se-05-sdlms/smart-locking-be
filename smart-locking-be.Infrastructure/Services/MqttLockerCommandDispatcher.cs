using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Protocol;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Infrastructure.Services;

public sealed class MqttLockerCommandDispatcher(IConfiguration configuration) : ILockerCommandDispatcher
{
    public async Task DispatchUnlockAsync(
        LockerUnlockCommand command,
        CancellationToken cancellationToken = default)
    {
        string host = configuration["Mqtt:Host"]?.Trim() ?? string.Empty;
        if (host.Length == 0)
        {
            throw new InvalidOperationException("MQTT broker chưa được cấu hình.");
        }

        int port = int.TryParse(configuration["Mqtt:Port"], out int configuredPort)
            ? configuredPort
            : 8883;
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"boxora-api-{Guid.NewGuid():N}")
            .WithTcpServer(host, port);

        string? username = configuration["Mqtt:Username"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            optionsBuilder.WithCredentials(username, configuration["Mqtt:Password"]);
        }
        bool useTls = !bool.TryParse(configuration["Mqtt:UseTls"], out bool configuredTls) || configuredTls;
        if (useTls)
        {
            optionsBuilder.WithTlsOptions(options => options.UseTls());
        }

        using var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(optionsBuilder.Build(), cancellationToken);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"boxora/lockers/{command.DeviceIdentifier}/commands/unlock")
            .WithPayload(JsonSerializer.Serialize(new
            {
                commandId = command.CommandId,
                hardwareChannel = command.HardwareChannel,
            }))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await client.PublishAsync(message, cancellationToken);
        await client.DisconnectAsync(cancellationToken: cancellationToken);
    }
}
