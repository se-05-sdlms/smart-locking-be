using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class NotificationRuleService(ApplicationDbContext dbContext) : INotificationRuleService
{
    public async Task<IReadOnlyCollection<NotificationRuleResponse>> GetAsync(
        Guid? systemPolicyId, bool? isEnabled, CancellationToken cancellationToken = default)
    {
        IQueryable<NotificationRule> query = dbContext.NotificationRules.AsNoTracking();
        if (systemPolicyId.HasValue) query = query.Where(item => item.SystemPolicyId == systemPolicyId);
        if (isEnabled.HasValue) query = query.Where(item => item.IsEnabled == isEnabled);
        return await query.OrderBy(item => item.EventType).ThenBy(item => item.Channel)
            .Select(item => new NotificationRuleResponse(item.Id, item.SystemPolicyId, item.EventType, item.Channel, item.LeadTimeMinutes, item.IsEnabled))
            .ToListAsync(cancellationToken);
    }

    public async Task<NotificationRuleResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.NotificationRules.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new NotificationRuleResponse(item.Id, item.SystemPolicyId, item.EventType, item.Channel, item.LeadTimeMinutes, item.IsEnabled))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Notification rule not found.");

    public async Task<NotificationRuleResponse> CreateAsync(
        Guid adminUserId,
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
        SystemPolicy policy = await dbContext.SystemPolicies.SingleOrDefaultAsync(
            candidate => candidate.Id == request.SystemPolicyId, cancellationToken)
            ?? throw new KeyNotFoundException("System policy not found.");
        if (policy.IsActive) throw new InvalidOperationException("Rules of an active policy are immutable.");
        if (await dbContext.NotificationRules.AnyAsync(rule =>
            rule.SystemPolicyId == request.SystemPolicyId &&
            rule.EventType == eventType && rule.Channel == request.Channel,
            cancellationToken))
            throw new InvalidOperationException("Notification rule already exists.");

        NotificationRule rule = new()
        {
            Id = Guid.NewGuid(),
            SystemPolicyId = request.SystemPolicyId,
            EventType = eventType,
            Channel = request.Channel,
            LeadTimeMinutes = request.LeadTimeMinutes,
            IsEnabled = request.IsEnabled
        };
        dbContext.NotificationRules.Add(rule);
        AddAudit(adminUserId, "NotificationRule.Created", rule.Id, eventType);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new NotificationRuleResponse(
            rule.Id, rule.SystemPolicyId, rule.EventType,
            rule.Channel, rule.LeadTimeMinutes, rule.IsEnabled);
    }

    public async Task<NotificationRuleResponse> UpdateAsync(
        Guid adminUserId, Guid id, UpdateNotificationRuleRequest request, CancellationToken cancellationToken = default)
    {
        NotificationRule rule = await dbContext.NotificationRules.Include(item => item.SystemPolicy)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Notification rule not found.");
        if (rule.SystemPolicy.IsActive) throw new InvalidOperationException("Rules of an active policy are immutable.");
        if (request.LeadTimeMinutes < 0) throw new ArgumentException("Lead time cannot be negative.");
        rule.LeadTimeMinutes = request.LeadTimeMinutes;
        rule.IsEnabled = request.IsEnabled;
        AddAudit(adminUserId, "NotificationRule.Updated", rule.Id, $"Enabled={rule.IsEnabled}");
        await dbContext.SaveChangesAsync(cancellationToken);
        return new NotificationRuleResponse(rule.Id, rule.SystemPolicyId, rule.EventType, rule.Channel, rule.LeadTimeMinutes, rule.IsEnabled);
    }

    private void AddAudit(Guid actor, string action, Guid id, string details) => dbContext.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(),
        ActorUserId = actor,
        Action = action,
        EntityType = nameof(NotificationRule),
        EntityId = id,
        Result = smart_locking_be.Domain.Enums.AuditLogResult.Succeeded,
        Details = details,
        OccurredAt = DateTimeOffset.UtcNow
    });
}
