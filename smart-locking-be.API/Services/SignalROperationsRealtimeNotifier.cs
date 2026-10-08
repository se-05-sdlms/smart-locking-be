using Microsoft.AspNetCore.SignalR;
using smart_locking_be.API.Hubs;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class SignalROperationsRealtimeNotifier(IHubContext<OperationsHub> hubContext)
    : IOperationsRealtimeNotifier
{
    public Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(OperationsHub.UserGroup(userId)).SendAsync("operationUpdated", message, cancellationToken);

    public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(OperationsHub.LockerGroup(lockerId)).SendAsync("operationUpdated", message, cancellationToken);
}
