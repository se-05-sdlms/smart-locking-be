using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.SystemPolicies;

public sealed record NotificationRuleResponse(
    Guid Id,
    string EventType,
    NotificationChannel Channel,
    int? LeadTimeMinutes,
    bool IsEnabled
);
