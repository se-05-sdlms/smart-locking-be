namespace smart_locking_be.Domain.Entities;

public sealed class OperatorAbsence
{
    public Guid Id { get; set; }
    public Guid OperatorUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}
