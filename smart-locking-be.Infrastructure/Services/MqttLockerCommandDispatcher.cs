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

        await PublishAsync($"lockers/{command.DeviceIdentifier}/doors/{command.HardwareChannel}",
            command.WorkflowCommand ?? (command.OperatorOverride ? "OPEN:OPERATOR" : command.DeliveryId.HasValue ? $"OPEN:{command.DeliveryId.Value:N}" : "OPEN"), cancellationToken);
    }

    // Reset/thu hồi không dùng nhánh tạo OPEN và không được báo thành công giả ở chế độ mô phỏng.
    public Task DispatchResetAsync(string device, int channel, string command, CancellationToken ct = default)
    {
        if (bool.TryParse(configuration["Mqtt:Simulate"], out bool simulate) && simulate)
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Không gửi lệnh đặt lại thật trong chế độ mô phỏng.");
        return PublishAsync($"lockers/{device}/doors/{channel}", command, ct);
    }

    private async Task PublishAsync(string topic, string payload, CancellationToken ct)
    {
        var options = BuildMqttClientOptions();
        try
        {
            using var client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(options, ct);
            await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), ct);
            await client.DisconnectAsync(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            throw new ServiceUnavailableException(LockerErrorCodes.OpenFailed, "Cannot connect to the locker.", exception);
        }
    }

    private MqttClientOptions BuildMqttClientOptions()
    {
        string host = configuration["Mqtt:Host"]?.Trim() ?? string.Empty;
        if (host.Length == 0)
        {
            throw new InternalServerException("MQTT broker is not configured.");
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
