namespace smart_locking_be.Application.DTOs.Notifications;

public sealed record RealtimeEvent(
    string Type,
    Guid? EntityId,
    Guid? LockerId,
    string Status,
    string? Message,
    DateTimeOffset OccurredAt);
