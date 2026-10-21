using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class LockerConnectionMonitor(
    ApplicationDbContext dbContext,
    IOperationsRealtimeNotifier realtimeNotifier,
    TimeProvider timeProvider)
{
    public async Task RecordSignalAsync(string deviceIdentifier, LockerConnectionStatus status, CancellationToken cancellationToken)
    {
        Locker? locker = await dbContext.Lockers.SingleOrDefaultAsync(
            item => item.DeviceIdentifier == deviceIdentifier, cancellationToken);
        if (locker is null) return;

        await ChangeStatusAsync(locker, status, cancellationToken);
    }

    public async Task MarkStaleOfflineAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - timeout;
        List<Locker> stale = await dbContext.Lockers
            .Where(item => item.ConnectionStatus != LockerConnectionStatus.Offline &&
                (item.LastSeenAt ?? item.CreatedAt) < cutoff)
            .ToListAsync(cancellationToken);

        foreach (Locker locker in stale)
        {
            await ChangeStatusAsync(locker, LockerConnectionStatus.Offline, cancellationToken);
        }
    }

    private async Task ChangeStatusAsync(Locker locker, LockerConnectionStatus status, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        LockerConnectionStatus previous = locker.ConnectionStatus;
        if (status == LockerConnectionStatus.Online) locker.LastSeenAt = now;
        if (previous == status)
        {
            if (status == LockerConnectionStatus.Online) await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        locker.ConnectionStatus = status;
        locker.UpdatedAt = now;
        dbContext.LockerEvents.Add(new LockerEvent
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            EventType = LockerEventType.ConnectionChanged,
            PreviousValue = previous.ToString(),
            NewValue = status.ToString(),
            Severity = status == LockerConnectionStatus.Offline ? LockerEventSeverity.Warning : LockerEventSeverity.Info,
            OccurredAt = now,
            ReceivedAt = now
        });

        List<Guid> recipients = [];
        if (status == LockerConnectionStatus.Offline)
        {
            recipients = await dbContext.OperatorAssignments
                .Where(item => item.LockerId == locker.Id && item.RevokedAt == null &&
                    item.OperatorUser.Status == UserStatus.Active)
                .Select(item => item.OperatorUserId)
                .ToListAsync(cancellationToken);
            if (recipients.Count == 0)
            {
                recipients = await dbContext.Users
                    .Where(item => item.Role == UserRole.Administrator && item.Status == UserStatus.Active)
                    .Select(item => item.Id)
                    .ToListAsync(cancellationToken);
            }
            foreach (Guid userId in recipients)
            {
                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Type = "LockerOffline",
                    Channel = NotificationChannel.InApp,
                    Title = "Locker disconnected",
                    Message = $"Locker {locker.Code} is offline.",
                    DeliveryStatus = NotificationDeliveryStatus.Sent,
                    SentAt = now,
                    CreatedAt = now
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        RealtimeEvent message = new("LockerConnectionChanged", locker.Id, locker.Id, status.ToString(),
            $"Locker {locker.Code} is {status}.", now);
        await realtimeNotifier.PublishToLockerAsync(locker.Id, message, cancellationToken);
        foreach (Guid userId in recipients)
            await realtimeNotifier.PublishToUserAsync(userId, message, cancellationToken);
    }
}
