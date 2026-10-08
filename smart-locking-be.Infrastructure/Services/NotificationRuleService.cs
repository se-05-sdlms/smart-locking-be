using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class NotificationRuleService(ApplicationDbContext dbContext) : INotificationRuleService
{
    public async Task<NotificationRuleResponse> CreateAsync(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        string eventType = request.EventType.Trim();
        if (eventType.Length is < 1 or > 100)
            throw new ArgumentException("Event type must contain between 1 and 100 characters.");
        if (!Enum.IsDefined(request.Channel))
            throw new ArgumentException("Notification channel is invalid.");
        if (request.LeadTimeMinutes < 0)
            throw new ArgumentException("Lead time cannot be negative.");
        if (!await dbContext.SystemPolicies.AnyAsync(
            policy => policy.Id == request.SystemPolicyId, cancellationToken))
            throw new KeyNotFoundException("System policy not found.");
        if (await dbContext.NotificationRules.AnyAsync(rule =>
            rule.SystemPolicyId == request.SystemPolicyId &&
            rule.EventType == eventType && rule.Channel == request.Channel,
            cancellationToken))
            throw new InvalidOperationException("Notification rule already exists.");

        NotificationRule rule = new()
        {
            Id = Guid.NewGuid(), SystemPolicyId = request.SystemPolicyId,
            EventType = eventType, Channel = request.Channel,
            LeadTimeMinutes = request.LeadTimeMinutes, IsEnabled = request.IsEnabled
        };
        dbContext.NotificationRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new NotificationRuleResponse(
            rule.Id, rule.SystemPolicyId, rule.EventType,
            rule.Channel, rule.LeadTimeMinutes, rule.IsEnabled);
    }
}
