using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class IncidentAction
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid ActionByUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public IncidentStatus? FromStatus { get; set; }
    public IncidentStatus? ToStatus { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Incident Incident { get; set; } = null!;
    public User ActionByUser { get; set; } = null!;
}
