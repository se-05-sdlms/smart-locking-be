using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public AuditLogResult Result { get; set; }
    public string? IpAddress { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public User? ActorUser { get; set; }
}
