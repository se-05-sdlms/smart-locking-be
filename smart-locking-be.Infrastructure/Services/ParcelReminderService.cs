using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ParcelReminderService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IPushNotificationService pushNotificationService) : IParcelReminderService
{
    public async Task<int> SendDailyRemindersAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        (DateTimeOffset dayStart, DateTimeOffset dayEnd) = ParcelTimeline.GetLocalDayWindow(now);
        List<Parcel> parcels = await dbContext.Parcels
            .Include(item => item.DeliveryRequest).ThenInclude(item => item.SystemPolicy)
            .Include(item => item.DeliveryRequest).ThenInclude(item => item.ResidentProfile)
            .Include(item => item.DeliveryRequest).ThenInclude(item => item.Locker)
            .Where(item => (item.Status == ParcelStatus.Stored || item.Status == ParcelStatus.Overdue) &&
                           item.MaxStorageUntil >= now)
            .ToListAsync(cancellationToken);
        List<Guid> pushIds = [];
        foreach (Parcel parcel in parcels)
        {
            SystemPolicy policy = parcel.DeliveryRequest.SystemPolicy;
            (DateTimeOffset countStart, _, _) = ParcelTimeline.GetDeadlines(
                parcel.StoredAt, policy.OverdueStartAfterHours, policy.MaxStorageHours);
            DateTimeOffset reminderStart = countStart.AddDays(Math.Max(policy.PickupReminderStartDay, 1) - 1);
            bool alreadySentToday = await dbContext.Notifications.AnyAsync(notification =>
                notification.ParcelId == parcel.Id &&
                notification.Type == "ParcelPickupReminder" &&
                notification.Channel == NotificationChannel.InApp &&
                notification.CreatedAt >= dayStart && notification.CreatedAt < dayEnd,
                cancellationToken);
            if (now < reminderStart || alreadySentToday)
                continue;

            int daysUntilTransfer = Math.Max(0, (int)Math.Ceiling((parcel.MaxStorageUntil - now).TotalDays));
            ResidentProfile resident = parcel.DeliveryRequest.ResidentProfile
                ?? throw new InvalidOperationException("Parcel does not have a resident owner.");
            pushIds.Add(pushNotificationService.EnqueueParcelPickupReminder(
                resident.UserId,
                parcel.DeliveryRequestId,
                parcel.Id,
                parcel.DeliveryRequest.Locker.Code,
                daysUntilTransfer,
                now >= parcel.PickupDueAt));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (Guid pushId in pushIds)
            await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        return pushIds.Count;
    }
}
