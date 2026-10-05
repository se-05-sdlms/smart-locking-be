using Microsoft.EntityFrameworkCore;
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
    public async Task<IReadOnlyCollection<NotificationResponse>> GetForUserAsync(
        Guid userId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken)
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

        return await query
            .OrderByDescending(notification => notification.CreatedAt)
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
    }

    public async Task<NotificationResponse> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        Notification notification = await dbContext.Notifications.SingleOrDefaultAsync(
            candidate =>
                candidate.Id == notificationId &&
                candidate.UserId == userId &&
                candidate.Channel == NotificationChannel.InApp,
            cancellationToken) ?? throw new KeyNotFoundException("Notification not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = timeProvider.GetUtcNow();
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

    public async Task<NotificationRuleResponse> CreateRuleAsync(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken)
    {
        string eventType = request.EventType.Trim();
        if (eventType.Length is < 1 or > 100)
        {
            throw new ArgumentException("Event type must contain between 1 and 100 characters.");
        }
        if (!Enum.IsDefined(request.Channel))
        {
            throw new ArgumentException("Notification channel is invalid.");
        }
        if (request.LeadTimeMinutes < 0)
        {
            throw new ArgumentException("Lead time cannot be negative.");
        }
        if (!await dbContext.SystemPolicies.AnyAsync(
            policy => policy.Id == request.SystemPolicyId,
            cancellationToken))
        {
            throw new KeyNotFoundException("System policy not found.");
        }
        if (await dbContext.NotificationRules.AnyAsync(rule =>
            rule.SystemPolicyId == request.SystemPolicyId &&
            rule.EventType == eventType &&
            rule.Channel == request.Channel,
            cancellationToken))
        {
            throw new InvalidOperationException("Notification rule already exists.");
        }

        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            SystemPolicyId = request.SystemPolicyId,
            EventType = eventType,
            Channel = request.Channel,
            LeadTimeMinutes = request.LeadTimeMinutes,
            IsEnabled = request.IsEnabled
        };
        dbContext.NotificationRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(rule);
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

    private static NotificationRuleResponse ToResponse(NotificationRule rule) => new(
        rule.Id,
        rule.SystemPolicyId,
        rule.EventType,
        rule.Channel,
        rule.LeadTimeMinutes,
        rule.IsEnabled);
}
