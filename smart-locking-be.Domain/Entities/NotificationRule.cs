using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class NotificationRule
{
    public Guid Id { get; set; }
    public Guid SystemPolicyId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; }
    public int? LeadTimeMinutes { get; set; }
    public bool IsEnabled { get; set; }

    public SystemPolicy SystemPolicy { get; set; } = null!;
}
