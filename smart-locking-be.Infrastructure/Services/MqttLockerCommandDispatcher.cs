using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Protocol;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Infrastructure.Services;

public sealed class MqttLockerCommandDispatcher(IConfiguration configuration) : ILockerCommandDispatcher, ICompartmentResetDispatcher
{
    public async Task DispatchUnlockAsync(
        LockerUnlockCommand command,
        CancellationToken cancellationToken = default)
    {
        if (bool.TryParse(configuration["Mqtt:Simulate"], out bool simulate) && simulate)
        {
            return;
        }

        var options = BuildMqttClientOptions();
        using var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(options, cancellationToken);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"lockers/{command.DeviceIdentifier}/doors/{command.HardwareChannel}")
            .WithPayload(command.WorkflowCommand ?? (command.OperatorOverride ? "OPEN:OPERATOR" : command.DeliveryId.HasValue ? $"OPEN:{command.DeliveryId.Value:N}" : "OPEN"))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await client.PublishAsync(message, cancellationToken);
        await client.DisconnectAsync(cancellationToken: cancellationToken);
    }

    // Lệnh đặt lại trạng thái không đi qua nhánh tạo lệnh mở relay.
    public async Task DispatchResetAsync(string device, int channel, string command, CancellationToken ct = default)
    {
        if (bool.TryParse(configuration["Mqtt:Simulate"], out bool simulate) && simulate)
            throw new InvalidOperationException("Không gửi lệnh đặt lại thật trong chế độ mô phỏng.");
        using var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(BuildMqttClientOptions(), ct);
        await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"lockers/{device}/doors/{channel}")
            .WithPayload(command).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), ct);
        await client.DisconnectAsync(cancellationToken: ct);
    }

    private MqttClientOptions BuildMqttClientOptions()
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
            optionsBuilder.WithTlsOptions(options =>
            {
                options.UseTls();

            });
        }
        return optionsBuilder.Build();
    }
}
