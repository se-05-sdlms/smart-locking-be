using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Infrastructure.Persistence;

public sealed class EffectiveOperatorAssignment
{
    public Guid OperatorUserId { get; init; }
    public Guid LockerId { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
}

public static class EffectiveOperatorAssignmentQueries
{
    internal const long AllocationLock = 19520261028;
    // All permission checks and routing use the same interval, independent of worker timeliness.
    public static IQueryable<EffectiveOperatorAssignment> EffectiveOperatorAssignments(
        this ApplicationDbContext db, DateTimeOffset? at = null)
    {
        DateTimeOffset now = at ?? DateTimeOffset.UtcNow;
        var covers = db.TemporaryOperatorAssignments.Where(x =>
            x.Status == TemporaryAssignmentStatus.Active && x.ActivatedAt != null && x.OperatorUserId != null &&
            x.Absence.CancelledAt == null && x.Absence.StartsAt <= now && now < x.Absence.EndsAt &&
            x.BaseAssignment.RevokedAt == null && x.BaseAssignment.LockerId == x.LockerId &&
            x.BaseAssignment.OperatorUserId == x.Absence.OperatorUserId &&
            db.SystemPolicies.Any(p => p.Id == x.PolicyId && p.IsActive && p.EffectiveFrom <= now &&
                (p.EffectiveTo == null || now < p.EffectiveTo)) &&
            db.Lockers.Any(l => l.Id == x.LockerId && l.LocationRevision == x.TargetLocationRevision) &&
            db.Lockers.Any(l => l.Id == x.AnchorLockerId && l.LocationRevision == x.AnchorLocationRevision) &&
            db.OperatorAssignments.Any(a => a.LockerId == x.AnchorLockerId && a.OperatorUserId == x.OperatorUserId &&
                a.RevokedAt == null && a.AssignedAt <= now));
        // One projected row per original assignment avoids set-operation/client-projection ambiguity.
        var rows = db.OperatorAssignments.Where(x => x.RevokedAt == null && x.AssignedAt <= now)
            .Select(x => new EffectiveOperatorAssignment
            {
                OperatorUserId = covers.Where(c => c.BaseAssignmentId == x.Id)
                    .OrderBy(c => c.Id).Select(c => c.OperatorUserId).FirstOrDefault() ?? x.OperatorUserId,
                LockerId = x.LockerId,
                RevokedAt = null
            });
        return rows.Where(x => db.Users.Any(u => u.Id == x.OperatorUserId &&
            u.Status == UserStatus.Active && u.Role == UserRole.LockerOperator) &&
            !db.OperatorAbsences.Any(a => a.OperatorUserId == x.OperatorUserId && a.CancelledAt == null &&
                a.StartsAt <= now && now < a.EndsAt));
    }
}
