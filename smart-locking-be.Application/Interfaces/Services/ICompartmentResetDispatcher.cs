namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>Cổng gửi lệnh; chi tiết kết nối MQTT được triển khai tại Infrastructure.</summary>
public interface ICompartmentResetDispatcher
{
    Task DispatchResetAsync(string device, int channel, string command, CancellationToken ct = default);
}
