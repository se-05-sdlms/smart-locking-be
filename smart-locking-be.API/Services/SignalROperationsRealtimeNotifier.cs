using Microsoft.EntityFrameworkCore;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using smart_locking_be.API.Hubs;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class SignalROperationsRealtimeNotifier(
    IHubContext<OperationsHub> hubContext,
    ILogger<SignalROperationsRealtimeNotifier> logger,
    ApplicationDbContext dbContext, TimeProvider timeProvider)
    : IOperationsRealtimeNotifier
{
    public Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(
            () => hubContext.Clients.Group(OperationsHub.UserGroup(userId)).SendAsync("operationUpdated", message, cancellationToken),
            $"user:{userId}");

    public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(async () =>
        {
            var operators = dbContext.EffectiveOperatorAssignments(timeProvider.GetUtcNow())
                .Where(x => x.LockerId == lockerId).Select(x => x.OperatorUserId);
            var admins = dbContext.Users.Where(x => x.Role == UserRole.Administrator && x.Status == UserStatus.Active).Select(x => x.Id);
            var recipients = await operators.Union(admins).ToListAsync(cancellationToken);
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
