namespace smart_locking_be.Domain.Entities;

public sealed class ResidentAccountChange
{
    public Guid Id { get; set; }
    public Guid ResidentUserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = "Submitted";
    public string Reason { get; set; } = string.Empty;
    public Guid? OriginalLockerId { get; set; }
    public Guid? TargetLockerId { get; set; }
    public string? OriginalPhoneNumber { get; set; }
    public string? NewPhoneNumber { get; set; }
    public DateTimeOffset? PhoneVerificationExpiresAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public User ResidentUser { get; set; } = null!;
    public Locker? OriginalLocker { get; set; }
    public Locker? TargetLocker { get; set; }
    public User? ReviewedByUser { get; set; }
    public ICollection<ResidentAccountChangeEvent> Events { get; set; } = new List<ResidentAccountChangeEvent>();
}

public sealed class ResidentAccountChangeEvent
{
    public Guid Id { get; set; }
    public Guid ResidentAccountChangeId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ResidentAccountChange ResidentAccountChange { get; set; } = null!;
    public User ActorUser { get; set; } = null!;
}
