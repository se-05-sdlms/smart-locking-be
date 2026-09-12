using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class PaymentTransaction
{
    public Guid Id { get; set; }
    public Guid OverdueChargeId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentTransactionStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public OverdueCharge OverdueCharge { get; set; } = null!;
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
