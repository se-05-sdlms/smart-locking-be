using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerDeviceEventHandler
{
    Task HandleCommandAckAsync(string deviceId, Guid commandId, bool ok, CancellationToken ct);
    Task HandleDoorChangedAsync(string deviceId, int hardwareChannel, DoorStatus state, DateTimeOffset at, CancellationToken ct);
    Task HandleConnectionChangedAsync(string deviceId, bool online, CancellationToken ct);
}
