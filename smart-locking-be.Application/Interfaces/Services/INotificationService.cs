using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Notifications;

namespace smart_locking_be.Application.Interfaces.Services;

public interface INotificationService
{
    Task<PagedResult<NotificationResponse>> GetForUserAsync(
        Guid userId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken,
        int pageNumber = 1);

    Task<NotificationResponse> SetReadStateAsync(
        Guid userId,
        Guid notificationId,
        bool isRead,
        CancellationToken cancellationToken);

    Task<MarkAllNotificationsReadResponse> MarkAllReadAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
