using smart_locking_be.Application.DTOs.Notifications;

namespace smart_locking_be.Application.Interfaces.Services;

public interface INotificationRuleService
{
    Task<IReadOnlyCollection<NotificationRuleResponse>> GetAsync(Guid? systemPolicyId, bool? isEnabled, CancellationToken cancellationToken = default);
    Task<NotificationRuleResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NotificationRuleResponse> CreateAsync(
        Guid adminUserId,
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken = default);
    Task<NotificationRuleResponse> UpdateAsync(Guid adminUserId, Guid id, UpdateNotificationRuleRequest request, CancellationToken cancellationToken = default);
}
