namespace smart_locking_be.Domain.Entities;

public sealed class PaymentReconciliation
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid ResidentUserId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Submitted";
    public Guid? AssignedAdminUserId { get; set; }
    public string? Response { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderCheckedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public User ResidentUser { get; set; } = null!;
    public User? AssignedAdminUser { get; set; }
    public ResidentSubscription? Subscription { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }
    public ICollection<PaymentReconciliationEvent> Events { get; set; } = new List<PaymentReconciliationEvent>();
}

public sealed class PaymentReconciliationEvent
{
    public Guid Id { get; set; }
    public Guid PaymentReconciliationId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    // Only order, amount, provider status and references; never bank account or credential data.
    public string? ProviderEvidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public PaymentReconciliation PaymentReconciliation { get; set; } = null!;
    public User ActorUser { get; set; } = null!;
}
