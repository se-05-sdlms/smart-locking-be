using smart_locking_be.Application.DTOs.Notifications;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOperationsRealtimeNotifier
{
    Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default);
    Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default);
}
