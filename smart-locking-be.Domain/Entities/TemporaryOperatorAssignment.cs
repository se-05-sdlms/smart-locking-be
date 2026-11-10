namespace smart_locking_be.Domain.Entities;

public enum TemporaryAssignmentStatus { PendingApproval, Approved, Active, NeedsApproval, Unassigned, Expired, Cancelled }

public sealed class TemporaryOperatorAssignment
{
    public Guid Id { get; set; }
    public Guid AbsenceId { get; set; }
    public OperatorAbsence Absence { get; set; } = null!;
    public Guid BaseAssignmentId { get; set; }
    public OperatorAssignment BaseAssignment { get; set; } = null!;
    public Guid LockerId { get; set; }
    public Guid? OperatorUserId { get; set; }
    public Guid? AnchorLockerId { get; set; }
    public Guid? TargetLocationRevision { get; set; }
    public Guid? AnchorLocationRevision { get; set; }
    public double? DistanceKm { get; set; }
    public int? LoadBefore { get; set; }
    public int? LoadAfter { get; set; }
    public string? ServiceArea { get; set; }
    public Guid? PolicyId { get; set; }
    public string? SelectionFingerprint { get; set; }
    public TemporaryAssignmentStatus Status { get; set; }
    public string StatusReason { get; set; } = string.Empty;
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
