using smart_locking_be.Application.DTOs.Notifications;

namespace smart_locking_be.Application.Interfaces.Services;

public interface INotificationService
{
    Task<IReadOnlyCollection<NotificationResponse>> GetForUserAsync(
        Guid userId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken);

    Task<NotificationResponse> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken);

    Task<MarkAllNotificationsReadResponse> MarkAllReadAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<NotificationRuleResponse> CreateRuleAsync(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken);
}
