using smart_locking_be.Application.DTOs.Notifications;

namespace smart_locking_be.Application.Interfaces.Services;

public interface INotificationRuleService
{
    Task<NotificationRuleResponse> CreateAsync(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken = default);
}
