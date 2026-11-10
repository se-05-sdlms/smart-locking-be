using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record RecentAuditLogResponse(
    Guid Id,
    string Action,
    string ActorName,
    string? Target,
    AuditLogResult Result,
    DateTimeOffset OccurredAt
);
