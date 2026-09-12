using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class MaintenanceActivity
{
    public Guid Id { get; set; }
    public Guid MaintenanceRequestId { get; set; }
    public Guid ActionByUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public MaintenanceStatus? FromStatus { get; set; }
    public MaintenanceStatus? ToStatus { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    public User ActionByUser { get; set; } = null!;
}
