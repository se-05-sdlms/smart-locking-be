using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class NotificationService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : INotificationService
{
    public async Task<PagedResult<NotificationResponse>> GetForUserAsync(
        Guid userId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken,
        int pageNumber = 1)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentException("Limit must be between 1 and 100.", nameof(limit));
        }

        IQueryable<Notification> query = dbContext.Notifications
            .AsNoTracking()
            .Where(notification =>
                notification.UserId == userId &&
                notification.Channel == NotificationChannel.InApp);
        if (unreadOnly)
        {
            query = query.Where(notification => !notification.IsRead);
        }

        pageNumber = Math.Max(pageNumber, 1);
        int totalCount = await query.CountAsync(cancellationToken);
        List<NotificationResponse> items = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip((pageNumber - 1) * limit)
            .Take(limit)
            .Select(notification => new NotificationResponse(
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.DeliveryRequestId,
                notification.ReturnRequestId,
                notification.ParcelId,
                notification.IncidentId,
                notification.PaymentTransactionId,
                notification.IsRead,
                notification.ReadAt,
                notification.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationResponse>(items, totalCount, pageNumber, limit);
    }

    public async Task<NotificationResponse> SetReadStateAsync(
        Guid userId,
        Guid notificationId,
        bool isRead,
        CancellationToken cancellationToken)
    {
        Notification notification = await dbContext.Notifications.SingleOrDefaultAsync(
            candidate =>
                candidate.Id == notificationId &&
                candidate.UserId == userId &&
                candidate.Channel == NotificationChannel.InApp,
            cancellationToken) ?? throw new KeyNotFoundException("Notification not found.");

        if (notification.IsRead != isRead)
        {
            notification.IsRead = isRead;
            notification.ReadAt = isRead ? timeProvider.GetUtcNow() : null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(notification);
    }

    public async Task<MarkAllNotificationsReadResponse> MarkAllReadAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        List<Notification> notifications = await dbContext.Notifications
            .Where(notification =>
                notification.UserId == userId &&
                notification.Channel == NotificationChannel.InApp &&
                !notification.IsRead)
            .ToListAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (Notification notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MarkAllNotificationsReadResponse(notifications.Count);
    }

    private static NotificationResponse ToResponse(Notification notification) => new(
        notification.Id,
        notification.Type,
        notification.Title,
        notification.Message,
        notification.DeliveryRequestId,
        notification.ReturnRequestId,
        notification.ParcelId,
        notification.IncidentId,
        notification.PaymentTransactionId,
        notification.IsRead,
        notification.ReadAt,
        notification.CreatedAt);

}
