using Microsoft.AspNetCore.SignalR;
using smart_locking_be.API.Hubs;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class SignalROperationsRealtimeNotifier(
    IHubContext<OperationsHub> hubContext,
    ILogger<SignalROperationsRealtimeNotifier> logger,
    IOperationsRealtimeAudience audience, TimeProvider timeProvider, LockerStatusStream statusStream)
    : IOperationsRealtimeNotifier
{
    public Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(
            () => hubContext.Clients.Group(OperationsHub.UserGroup(userId)).SendAsync("operationUpdated", message, cancellationToken),
            $"user:{userId}");

    public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(async () =>
        {
            // Đẩy tín hiệu công khai trước truy vấn người nhận vận hành; SSE chỉ trả snapshot tối thiểu.
            if (statusStream.HasSubscribers)
            {
                string? device = await audience.GetDeviceIdentifierAsync(lockerId, cancellationToken);
                if (device is not null) statusStream.Publish(device);
            }
            var recipients = await audience.GetRecipientIdsAsync(lockerId, timeProvider.GetUtcNow(), cancellationToken);
            foreach (Guid userId in recipients) await PublishToUserAsync(userId, message, cancellationToken);
        }, $"locker:{lockerId}");

    private async Task PublishAsync(Func<Task> publish, string target)
    {
        try
        {
            await publish();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish realtime operation update to {Target}.", target);
        }
    }
}
