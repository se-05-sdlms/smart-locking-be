using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OperatorCoverageService(ApplicationDbContext db, TimeProvider clock) : IOperatorCoverageService
{
    // A DB advisory transaction lock also serializes separate API/job processes.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<LockerLocationResponse> GetLocationAsync(Guid lockerId, CancellationToken ct) =>
        Location(await FindLockerAsync(lockerId, ct));

    public Task<LockerLocationResponse> ConfirmLocationAsync(Guid adminId, Guid lockerId,
        ConfirmLockerLocationRequest request, CancellationToken ct) => SerializedAsync(async () =>
    {
        if (!double.IsFinite(request.Latitude) || request.Latitude is < -90 or > 90 ||
            !double.IsFinite(request.Longitude) || request.Longitude is < -180 or > 180 ||
            string.IsNullOrWhiteSpace(request.ServiceArea) || request.ServiceArea.Trim().Length > 100)
            throw BadRequestException.For("location", "Valid latitude, longitude and service area are required.");
        Locker locker = await FindLockerAsync(lockerId, ct);
        locker.Latitude = request.Latitude;
        locker.Longitude = request.Longitude;
        locker.ServiceArea = request.ServiceArea.Trim();
        locker.LocationConfirmedAt = clock.GetUtcNow();
        locker.LocationConfirmedByUserId = adminId;
        locker.LocationRevision = Guid.NewGuid();
        locker.UpdatedAt = clock.GetUtcNow();
        var affected = await db.TemporaryOperatorAssignments.Include(x => x.Absence)
            .Where(x => (x.Status == TemporaryAssignmentStatus.Approved || x.Status == TemporaryAssignmentStatus.Active) &&
                (x.LockerId == lockerId || x.AnchorLockerId == lockerId)).ToListAsync(ct);
        foreach (var proposal in affected)
            RequireReapproval(proposal, adminId, "Confirmed locker location changed.");
        Audit(adminId, "Locker.LocationConfirmed", lockerId, $"Area={locker.ServiceArea}; lat={request.Latitude}; lng={request.Longitude}");
        await db.SaveChangesAsync(ct);
        return Location(locker);
    }, ct);

    public async Task<IReadOnlyCollection<OperatorAbsenceResponse>> ListAsync(Guid actorId, string role, CancellationToken ct)
    {
        RequireRole(role);
        var query = db.OperatorAbsences.AsNoTracking();
        if (role != nameof(UserRole.Administrator)) query = query.Where(x => x.OperatorUserId == actorId);
        var absences = await query.OrderByDescending(x => x.StartsAt).Take(200).ToListAsync(ct);
        List<OperatorAbsenceResponse> result = [];
        foreach (var absence in absences) result.Add(await MapAsync(absence, ct));
        return result;
    }

    public Task<OperatorAbsenceResponse> CreateAsync(Guid actorId, string role, CreateOperatorAbsenceRequest request,
        CancellationToken ct) => SerializedAsync(async () =>
    {
        RequireRole(role);
        if (role != nameof(UserRole.Administrator) && request.OperatorUserId != actorId)
            throw new ForbiddenException("OPERATOR_ABSENCE_ACCESS_DENIED", "You can only report your own absence.");
        DateTimeOffset starts = request.StartsAt.ToUniversalTime(), ends = request.EndsAt.ToUniversalTime();
        if (starts >= ends || ends <= clock.GetUtcNow())
            throw BadRequestException.For("interval", "Use a nonempty UTC interval ending in the future.");
        string reason = Reason(request.Reason);
        if (!await db.Users.AnyAsync(x => x.Id == request.OperatorUserId && x.Role == UserRole.LockerOperator &&
            x.Status == UserStatus.Active, ct))
            throw BadRequestException.For("operatorUserId", "An active locker operator is required.");
        if (await db.OperatorAbsences.AnyAsync(x => x.OperatorUserId == request.OperatorUserId && x.CancelledAt == null &&
            x.StartsAt < ends && starts < x.EndsAt, ct))
            throw new ConflictException("OPERATOR_ABSENCE_OVERLAP", "This operator already has an overlapping absence.");
        OperatorAbsence absence = new()
        {
            Id = Guid.NewGuid(),
            OperatorUserId = request.OperatorUserId,
            CreatedByUserId = actorId,
            StartsAt = starts,
            EndsAt = ends,
            Reason = reason,
            CreatedAt = clock.GetUtcNow()
        };
        db.OperatorAbsences.Add(absence);
        Audit(actorId, "OperatorAbsence.Created", absence.Id, $"{absence.OperatorUserId}: [{starts:O}, {ends:O}); {reason}");
        await db.SaveChangesAsync(ct);
        await SuggestCoreAsync(absence, false, ct);
        NotifyAdmins("Operator absence requires review", $"Absence {absence.Id}: [{starts:O}, {ends:O}). No replacement is enabled without approval.");
        await db.SaveChangesAsync(ct);
        return await MapAsync(absence, ct);
    }, ct);

    public Task<OperatorAbsenceResponse> SuggestAsync(Guid adminId, Guid absenceId, CancellationToken ct) =>
        SerializedAsync(async () =>
        {
            var absence = await FindAbsenceAsync(absenceId, ct);
            EnsureOpen(absence);
            await SuggestCoreAsync(absence, true, ct);
            Audit(adminId, "OperatorReplacement.Proposed", absenceId, "Recomputed pending proposals from original assignments.");
            await db.SaveChangesAsync(ct);
            return await MapAsync(absence, ct);
        }, ct);

    public Task<OperatorAbsenceResponse> ApproveAsync(Guid adminId, Guid proposalId,
        ReviewOperatorReplacementRequest request, CancellationToken ct) => SerializedAsync(async () =>
    {
        string reason = Reason(request.Reason);
        var proposal = await db.TemporaryOperatorAssignments.Include(x => x.Absence).Include(x => x.BaseAssignment)
            .SingleOrDefaultAsync(x => x.Id == proposalId, ct)
            ?? throw new NotFoundException("OPERATOR_REPLACEMENT_NOT_FOUND", "Proposal not found.");
        EnsureOpen(proposal.Absence);
        if (proposal.Status is TemporaryAssignmentStatus.Cancelled or TemporaryAssignmentStatus.Expired)
            throw new ConflictException("OPERATOR_REPLACEMENT_CLOSED", "Create a new absence to extend a completed interval.");
        Guid? previousOperatorId = proposal.OperatorUserId;
        bool wasActive = proposal.Status == TemporaryAssignmentStatus.Active;
        Guid candidateId = request.OperatorUserId ?? proposal.OperatorUserId
            ?? throw BadRequestException.For("operatorUserId", "Select an eligible operator.");
        var selection = await SelectAsync(proposal, candidateId, [], ct)
            ?? throw new ConflictException("OPERATOR_REPLACEMENT_INELIGIBLE", "Candidate, coordinates, policy or whole-interval capacity is no longer valid. Recompute proposals.");
        // Never silently accept a changed automatic suggestion: expose it for another explicit approval.
        if (request.OperatorUserId is null && proposal.SelectionFingerprint != selection.Fingerprint)
        {
            ApplySelection(proposal, selection);
            proposal.Status = TemporaryAssignmentStatus.NeedsApproval;
            proposal.ApprovedAt = null;
            proposal.ApprovedByUserId = null;
            proposal.ActivatedAt = null;
            proposal.StatusReason = "Selection conditions changed. Review the refreshed proposal and approve again.";
            Audit(adminId, "OperatorReplacement.ReapprovalRequired", proposal.Id, proposal.StatusReason);
            NotifyAdmins("Replacement needs reapproval", Detail(proposal));
        }
        else
        {
            ApplySelection(proposal, selection);
            proposal.Status = TemporaryAssignmentStatus.Approved;
            proposal.ApprovedByUserId = adminId;
            proposal.ApprovedAt = clock.GetUtcNow();
            proposal.ActivatedAt = null;
            proposal.StatusReason = reason;
            Audit(adminId, "OperatorReplacement.Approved", proposal.Id, $"Operator={candidateId}; {reason}");
            Notify(proposal.Absence.OperatorUserId, "Replacement approved", Detail(proposal));
            Notify(candidateId, "Temporary assignment approved", Detail(proposal));
            if (proposal.Absence.StartsAt <= clock.GetUtcNow()) await ActivateAsync(proposal, ct);
        }
        if (wasActive && previousOperatorId is { } oldOperator &&
            (oldOperator != proposal.OperatorUserId || proposal.Status != TemporaryAssignmentStatus.Active))
            Notify(oldOperator, "Temporary assignment revoked", Detail(proposal));
        await db.SaveChangesAsync(ct);
        return await MapAsync(proposal.Absence, ct);
    }, ct);

    public Task<OperatorAbsenceResponse> CancelAsync(Guid actorId, string role, Guid absenceId, string reason,
        CancellationToken ct) => SerializedAsync(async () =>
    {
        RequireRole(role);
        reason = Reason(reason);
        var absence = await FindAbsenceAsync(absenceId, ct);
        if (role != nameof(UserRole.Administrator) && absence.OperatorUserId != actorId)
            throw new ForbiddenException("OPERATOR_ABSENCE_ACCESS_DENIED", "This absence belongs to another operator.");
        // Once Admin has approved coverage, cancellation is an Admin operation too.
        var proposals = await db.TemporaryOperatorAssignments.Where(x => x.AbsenceId == absenceId).ToListAsync(ct);
        if (role != nameof(UserRole.Administrator) && proposals.Any(x => x.ApprovedAt != null))
            throw new ForbiddenException("OPERATOR_ABSENCE_ADMIN_REVIEW_REQUIRED", "Ask an administrator to cancel approved coverage.");
        if (absence.CancelledAt == null)
        {
            absence.CancelledAt = clock.GetUtcNow();
            foreach (var proposal in proposals.Where(x => x.CompletedAt == null))
            {
                proposal.Status = TemporaryAssignmentStatus.Cancelled;
                proposal.StatusReason = reason;
                proposal.CompletedAt = clock.GetUtcNow();
                Audit(actorId, "OperatorReplacement.Cancelled", proposal.Id, reason);
                if (proposal.OperatorUserId is { } substitute) Notify(substitute, "Temporary assignment revoked", Detail(proposal));
            }
            Notify(absence.OperatorUserId, "Absence cancelled", $"{absence.Id}; {reason}");
            Audit(actorId, "OperatorAbsence.Cancelled", absence.Id, reason);
            await db.SaveChangesAsync(ct);
            await NotifyInvalidRestorationAsync(absence, ct);
            await db.SaveChangesAsync(ct);
        }
        return await MapAsync(absence, ct);
    }, ct);

    public Task ReconcileAsync(CancellationToken ct) => SerializedAsync(async () =>
    {
        DateTimeOffset now = clock.GetUtcNow();
        var proposals = await db.TemporaryOperatorAssignments.Include(x => x.Absence).Include(x => x.BaseAssignment)
            .Where(x => x.CompletedAt == null).OrderBy(x => x.Absence.StartsAt).ThenBy(x => x.Id).ToListAsync(ct);
        foreach (var proposal in proposals)
        {
            if (proposal.Absence.CancelledAt != null || proposal.Absence.EndsAt <= now)
            {
                proposal.Status = proposal.Absence.CancelledAt != null ? TemporaryAssignmentStatus.Cancelled : TemporaryAssignmentStatus.Expired;
                proposal.CompletedAt = now;
                Audit(null, "OperatorReplacement." + proposal.Status, proposal.Id, Detail(proposal));
                if (proposal.OperatorUserId is { } substitute) Notify(substitute, "Temporary assignment ended", Detail(proposal));
                Notify(proposal.Absence.OperatorUserId, "Temporary assignment ended", Detail(proposal));
                await NotifyInvalidRestorationAsync(proposal.Absence, ct);
            }
            else if (proposal.Status == TemporaryAssignmentStatus.Approved && proposal.Absence.StartsAt <= now)
                await ActivateAsync(proposal, ct);
            else if (proposal.Status == TemporaryAssignmentStatus.Active)
            {
                var selection = proposal.OperatorUserId is { } current ? await SelectAsync(proposal, current, [], ct) : null;
                if (selection == null || selection.Fingerprint != proposal.SelectionFingerprint)
                {
                    RequireReapproval(proposal, null, "Active replacement is no longer eligible. Admin review is required.");
                }
            }
        }
        // A base locker added after the first batch still requires an explicit Admin review.
        var open = await db.OperatorAbsences.Where(x => x.CancelledAt == null && now < x.EndsAt).ToListAsync(ct);
        foreach (var absence in open) await SuggestCoreAsync(absence, false, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    private async Task ActivateAsync(TemporaryOperatorAssignment proposal, CancellationToken ct)
    {
        var selection = proposal.OperatorUserId is { } candidate ? await SelectAsync(proposal, candidate, [], ct) : null;
        if (selection == null || selection.Fingerprint != proposal.SelectionFingerprint)
        {
            RequireReapproval(proposal, null, "Conditions changed before activation. Admin must review again; radius was not widened.");
            return;
        }
        ApplySelection(proposal, selection);
        proposal.Status = TemporaryAssignmentStatus.Active;
        proposal.ActivatedAt = clock.GetUtcNow();
        Audit(proposal.ApprovedByUserId, "OperatorReplacement.Activated", proposal.Id, Detail(proposal));
        Notify(proposal.Absence.OperatorUserId, "Temporary coverage started", Detail(proposal));
        Notify(selection.OperatorId, "Temporary coverage started", Detail(proposal));
    }

    private async Task SuggestCoreAsync(OperatorAbsence absence, bool refreshPending, CancellationToken ct)
    {
        var baseAssignments = await db.OperatorAssignments.Where(x => x.OperatorUserId == absence.OperatorUserId &&
            x.RevokedAt == null && x.AssignedAt <= clock.GetUtcNow()).OrderBy(x => x.LockerId).ToListAsync(ct);
        var existing = await db.TemporaryOperatorAssignments.Where(x => x.AbsenceId == absence.Id).ToListAsync(ct);
        List<Selection> batch = [];
        foreach (var basis in baseAssignments)
        {
            var proposal = existing.SingleOrDefault(x => x.LockerId == basis.LockerId);
            if (proposal != null && (!refreshPending || proposal.Status is TemporaryAssignmentStatus.Approved or TemporaryAssignmentStatus.Active or
                TemporaryAssignmentStatus.Expired or TemporaryAssignmentStatus.Cancelled)) continue;
            if (proposal == null)
            {
                proposal = new TemporaryOperatorAssignment
                {
                    Id = Guid.NewGuid(),
                    AbsenceId = absence.Id,
                    Absence = absence,
                    BaseAssignmentId = basis.Id,
                    BaseAssignment = basis,
                    LockerId = basis.LockerId,
                    CreatedAt = clock.GetUtcNow()
                };
                db.TemporaryOperatorAssignments.Add(proposal);
            }
            var selected = await SelectAsync(proposal, null, batch, ct);
            proposal.ApprovedAt = null;
            proposal.ApprovedByUserId = null;
            proposal.ActivatedAt = null;
            if (selected == null)
            {
                proposal.OperatorUserId = null;
                proposal.AnchorLockerId = null;
                proposal.DistanceKm = null;
                proposal.SelectionFingerprint = null;
                proposal.Status = TemporaryAssignmentStatus.Unassigned;
                proposal.StatusReason = "Missing confirmed coordinates/policy or no eligible original anchor within radius and whole-interval capacity.";
            }
            else
            {
                ApplySelection(proposal, selected);
                proposal.Status = TemporaryAssignmentStatus.PendingApproval;
                proposal.StatusReason = "Suggestion only. Explicit Admin approval is required.";
                batch.Add(selected);
            }
        }
    }

    private sealed record Selection(Guid OperatorId, Guid AnchorId, double Distance, int Load, string Area, Guid PolicyId,
        Guid TargetRevision, Guid AnchorRevision, string Fingerprint);

    private async Task<Selection?> SelectAsync(TemporaryOperatorAssignment proposal, Guid? candidateId, List<Selection> batch, CancellationToken ct)
    {
        var absence = proposal.Absence;
        DateTimeOffset now = clock.GetUtcNow();
        var policy = await db.SystemPolicies.AsNoTracking().Where(x => x.IsActive && x.EffectiveFrom <= now &&
            (x.EffectiveTo == null || now < x.EffectiveTo)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (policy?.OperatorReplacementRadiusKm is not > 0 || policy.OperatorReplacementMaxLoad is not > 0 ||
            policy.OperatorReplacementAreas is not { Length: > 0 } || policy.OperatorReplacementEligibleUserIds is not { Length: > 0 }) return null;
        var basis = await db.OperatorAssignments.AsNoTracking().Include(x => x.Locker).Include(x => x.OperatorUser)
            .SingleOrDefaultAsync(x => x.Id == proposal.BaseAssignmentId && x.RevokedAt == null &&
                x.OperatorUserId == absence.OperatorUserId && x.LockerId == proposal.LockerId, ct);
        if (basis == null || !HasLocation(basis.Locker) || !policy.OperatorReplacementAreas.Contains(basis.Locker.ServiceArea!)) return null;
        var originals = await db.OperatorAssignments.AsNoTracking().Include(x => x.Locker).Include(x => x.OperatorUser)
            .Where(x => x.RevokedAt == null && x.AssignedAt <= now && x.OperatorUserId != absence.OperatorUserId &&
                x.OperatorUser.Status == UserStatus.Active && x.OperatorUser.Role == UserRole.LockerOperator).ToListAsync(ct);
        var unavailable = await db.OperatorAbsences.AsNoTracking().Where(x => x.CancelledAt == null &&
            x.StartsAt < absence.EndsAt && absence.StartsAt < x.EndsAt).Select(x => x.OperatorUserId).Distinct().ToListAsync(ct);
        var reservations = await db.TemporaryOperatorAssignments.AsNoTracking().Include(x => x.Absence)
            .Where(x => x.Id != proposal.Id && (x.Status == TemporaryAssignmentStatus.Approved || x.Status == TemporaryAssignmentStatus.Active) &&
                x.Absence.CancelledAt == null && x.Absence.StartsAt < absence.EndsAt && absence.StartsAt < x.Absence.EndsAt).ToListAsync(ct);
        if (reservations.Any(x => x.LockerId == proposal.LockerId)) return null;
        List<Selection> options = [];
        foreach (var group in originals.GroupBy(x => x.OperatorUserId))
        {
            Guid userId = group.Key;
            if (candidateId != null && candidateId != userId || unavailable.Contains(userId) ||
                !policy.OperatorReplacementEligibleUserIds.Contains(userId)) continue;
            var anchor = group.Where(x => HasLocation(x.Locker) && x.Locker.ServiceArea == basis.Locker.ServiceArea)
                .Select(x => new { Assignment = x, Distance = DistanceKm(basis.Locker, x.Locker) })
                .Where(x => x.Distance <= policy.OperatorReplacementRadiusKm.Value)
                .OrderBy(x => x.Distance).ThenBy(x => x.Assignment.LockerId).FirstOrDefault();
            if (anchor == null) continue;
            var ownReservations = reservations.Where(x => x.OperatorUserId == userId).ToList();
            var boundaries = ownReservations.Select(x => x.Absence.StartsAt < absence.StartsAt ? absence.StartsAt : x.Absence.StartsAt)
                .Append(absence.StartsAt).Distinct();
            int peak = boundaries.Max(t => ownReservations.Count(x => x.Absence.StartsAt <= t && t < x.Absence.EndsAt));
            int load = group.Count() + peak + batch.Count(x => x.OperatorId == userId);
            if (load + 1 > policy.OperatorReplacementMaxLoad.Value) continue;
            // Original anchors are snapshotted before the batch. Temporary assignments never become anchors.
            string source = string.Join("|", group.OrderBy(x => x.Id).Select(x => $"{x.Id:N}:{x.Locker.LocationRevision}"));
            string material = $"{policy.Id:N}|{basis.Id:N}|{basis.Locker.LocationRevision}|{userId:N}|{anchor.Assignment.LockerId:N}|{source}";
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
            options.Add(new Selection(userId, anchor.Assignment.LockerId, anchor.Distance, load, basis.Locker.ServiceArea!, policy.Id, basis.Locker.LocationRevision!.Value, anchor.Assignment.Locker.LocationRevision!.Value, fingerprint));
        }
        return options.OrderBy(x => x.Distance).ThenBy(x => x.Load).ThenBy(x => x.OperatorId).FirstOrDefault();
    }

    private static bool HasLocation(Locker x) => x.Latitude.HasValue && x.Longitude.HasValue &&
        x.LocationConfirmedAt.HasValue && x.LocationConfirmedByUserId.HasValue && x.LocationRevision.HasValue &&
        !string.IsNullOrWhiteSpace(x.ServiceArea);

    private static double DistanceKm(Locker a, Locker b)
    {
        const double rad = Math.PI / 180;
        double lat1 = a.Latitude!.Value * rad, lat2 = b.Latitude!.Value * rad;
        double dlat = lat2 - lat1, dlon = (b.Longitude!.Value - a.Longitude!.Value) * rad;
        double h = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 6371.0088 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }

    private static void ApplySelection(TemporaryOperatorAssignment p, Selection s)
    {
        p.OperatorUserId = s.OperatorId;
        p.AnchorLockerId = s.AnchorId;
        p.TargetLocationRevision = s.TargetRevision;
        p.AnchorLocationRevision = s.AnchorRevision;
        p.DistanceKm = s.Distance;
        p.LoadBefore = s.Load;
        p.LoadAfter = s.Load + 1;
        p.ServiceArea = s.Area;
        p.PolicyId = s.PolicyId;
        p.SelectionFingerprint = s.Fingerprint;
    }

    private void RequireReapproval(TemporaryOperatorAssignment proposal, Guid? actorId, string reason)
    {
        bool wasActive = proposal.Status == TemporaryAssignmentStatus.Active;
        proposal.Status = TemporaryAssignmentStatus.NeedsApproval;
        proposal.ActivatedAt = null;
        proposal.StatusReason = reason;
        Audit(actorId, "OperatorReplacement.ReapprovalRequired", proposal.Id, reason);
        NotifyAdmins("Replacement needs reapproval", Detail(proposal));
        if (wasActive && proposal.OperatorUserId is { } substitute)
            Notify(substitute, "Temporary coverage suspended", Detail(proposal));
    }

    private async Task NotifyInvalidRestorationAsync(OperatorAbsence absence, CancellationToken ct)
    {
        bool valid = await db.Users.AnyAsync(x => x.Id == absence.OperatorUserId && x.Status == UserStatus.Active && x.Role == UserRole.LockerOperator, ct) &&
            !await db.OperatorAbsences.AnyAsync(x => x.Id != absence.Id && x.OperatorUserId == absence.OperatorUserId &&
                x.CancelledAt == null && x.StartsAt <= clock.GetUtcNow() && clock.GetUtcNow() < x.EndsAt, ct);
        if (!valid) NotifyAdmins("Original operator cannot resume", $"Absence {absence.Id}: original operator is inactive or unavailable. Review locker assignments.");
    }

    private async Task<Locker> FindLockerAsync(Guid id, CancellationToken ct) =>
        await db.Lockers.SingleOrDefaultAsync(x => x.Id == id, ct) ??
        throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
    private async Task<OperatorAbsence> FindAbsenceAsync(Guid id, CancellationToken ct) =>
        await db.OperatorAbsences.SingleOrDefaultAsync(x => x.Id == id, ct) ??
        throw new NotFoundException("OPERATOR_ABSENCE_NOT_FOUND", "Absence not found.");
    private void EnsureOpen(OperatorAbsence absence)
    {
        if (absence.CancelledAt != null || absence.EndsAt <= clock.GetUtcNow())
            throw new ConflictException("OPERATOR_ABSENCE_CLOSED", "Absence is cancelled or has ended.");
    }
    private static string Reason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 1000 ? reason.Trim() :
        throw BadRequestException.For("reason", "Reason is required and must not exceed 1000 characters.");
    private static void RequireRole(string role)
    {
        if (role is not (nameof(UserRole.Administrator) or nameof(UserRole.LockerOperator)))
            throw new ForbiddenException("OPERATOR_ABSENCE_ACCESS_DENIED", "An operator or administrator is required.");
    }
    private static LockerLocationResponse Location(Locker x) => new(x.Id, x.Latitude, x.Longitude, x.ServiceArea, x.LocationConfirmedByUserId, x.LocationConfirmedAt);
    private async Task<OperatorAbsenceResponse> MapAsync(OperatorAbsence a, CancellationToken ct)
    {
        var proposals = await db.TemporaryOperatorAssignments.AsNoTracking().Include(x => x.BaseAssignment)
            .Where(x => x.AbsenceId == a.Id).OrderBy(x => x.LockerId).ToListAsync(ct);
        return new(a.Id, a.OperatorUserId, a.StartsAt, a.EndsAt, a.Reason, a.CancelledAt, proposals.Select(x =>
            new OperatorReplacementResponse(x.Id, x.LockerId, x.BaseAssignment.OperatorUserId, x.OperatorUserId, x.Status.ToString(),
                x.StatusReason, x.AnchorLockerId, x.DistanceKm, x.ServiceArea, x.LoadBefore, x.LoadAfter, x.PolicyId,
                x.ApprovedByUserId, x.ApprovedAt, x.ActivatedAt)).ToList());
    }
    private static string Detail(TemporaryOperatorAssignment p) =>
        $"Locker {p.LockerId}; operator {p.OperatorUserId}; [{p.Absence.StartsAt:O}, {p.Absence.EndsAt:O}); {p.Status}.";
    private void Notify(Guid userId, string title, string detail) => db.Notifications.Add(new Notification
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = "OperatorReplacement",
        Title = title,
        Message = detail,
        Channel = NotificationChannel.InApp,
        DeliveryStatus = NotificationDeliveryStatus.Sent,
        CreatedAt = clock.GetUtcNow(),
        SentAt = clock.GetUtcNow()
    });
    private void NotifyAdmins(string title, string detail)
    {
        foreach (var id in db.Users.Where(x => x.Role == UserRole.Administrator && x.Status == UserStatus.Active).Select(x => x.Id)) Notify(id, title, detail);
    }
    private void Audit(Guid? actorId, string action, Guid id, string detail) => db.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(),
        ActorUserId = actorId,
        Action = action,
        EntityType = "OperatorCoverage",
        EntityId = id,
        Details = detail,
        Result = AuditLogResult.Succeeded,
        OccurredAt = clock.GetUtcNow()
    });
    private async Task<T> SerializedAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
            if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EffectiveOperatorAssignmentQueries.AllocationLock})", ct);
            T result = await work();
            if (transaction != null) await transaction.CommitAsync(ct);
            return result;
        }
        finally { Gate.Release(); }
    }
}
