using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class OperatorTaskHandover
{
    public Guid Id { get; set; }
    public Guid LockerId { get; set; }
    public string TaskKind { get; set; } = string.Empty;
    public Guid TaskId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public Guid? IntendedReceiverUserId { get; set; }
    public string WorkDone { get; set; } = string.Empty;
    public string Blockers { get; set; } = string.Empty;
    public string NextSteps { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OperatorTaskAcknowledgement
{
    public Guid Id { get; set; }
    public Guid HandoverId { get; set; }
    public Guid ReceiverUserId { get; set; }
    public string ReceiverName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
