using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Message,
    Guid? DeliveryRequestId,
    Guid? ReturnRequestId,
    Guid? ParcelId,
    Guid? IncidentId,
    Guid? PaymentTransactionId,
    bool IsRead,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt);

public sealed record MarkAllNotificationsReadResponse(int UpdatedCount);

public sealed record UpdateNotificationRequest(bool IsRead);

public sealed record CreateNotificationRuleRequest(
    Guid SystemPolicyId,
    string EventType,
    NotificationChannel Channel,
    int? LeadTimeMinutes,
    bool IsEnabled);

public sealed record NotificationRuleResponse(
    Guid Id,
    Guid SystemPolicyId,
    string EventType,
    NotificationChannel Channel,
    int? LeadTimeMinutes,
    bool IsEnabled);
