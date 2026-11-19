using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Users;

public sealed class OperatorCoverageServiceUnitTests
{
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _originalOperatorId = Guid.NewGuid();
    private readonly Guid _candidateOperatorId = Guid.NewGuid();
    private readonly Guid _lockerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static OperatorCoverageService CreateService(ApplicationDbContext db, DateTimeOffset now) =>
        new(db, new FixedTimeProvider(now));

    private async Task<(OperatorAbsence absence, TemporaryOperatorAssignment proposal)> SeedDataAsync(ApplicationDbContext db)
    {
        var admin = new User { Id = _adminId, Email = "admin@test.com", Role = UserRole.Administrator, Status = UserStatus.Active };
        var op1 = new User { Id = _originalOperatorId, Email = "op1@test.com", Role = UserRole.LockerOperator, Status = UserStatus.Active };
        var op2 = new User { Id = _candidateOperatorId, Email = "op2@test.com", Role = UserRole.LockerOperator, Status = UserStatus.Active };
        var locker = new Locker { Id = _lockerId, Code = "LK-01", DeviceIdentifier = "dev-01", OperationalStatus = LockerOperationalStatus.Operational, ServiceArea = "Area 1" };

        var baseAssignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = _originalOperatorId,
            LockerId = _lockerId,
            AssignedByUserId = _adminId,
            AssignedAt = Now.AddDays(-10)
        };

        var absence = new OperatorAbsence
        {
            Id = Guid.NewGuid(),
            OperatorUserId = _originalOperatorId,
            CreatedByUserId = _originalOperatorId,
            StartsAt = Now.AddHours(1),
            EndsAt = Now.AddHours(5),
            Reason = "Medical leave",
            CreatedAt = Now
        };

        var proposal = new TemporaryOperatorAssignment
        {
            Id = Guid.NewGuid(),
            AbsenceId = absence.Id,
            Absence = absence,
            BaseAssignmentId = baseAssignment.Id,
            BaseAssignment = baseAssignment,
            LockerId = _lockerId,
            Status = TemporaryAssignmentStatus.Unassigned,
            StatusReason = "No candidate",
            CreatedAt = Now
        };

        db.Users.AddRange(admin, op1, op2);
        db.Lockers.Add(locker);
        db.OperatorAssignments.Add(baseAssignment);
        db.OperatorAbsences.Add(absence);
        db.TemporaryOperatorAssignments.Add(proposal);
        await db.SaveChangesAsync();

        return (absence, proposal);
    }

    [Fact]
    public async Task ApproveAsync_ManualSelection_WhenNoCoordinatesOrAnchor_SucceedsAsManualOverride()
    {
        await using var db = CreateDbContext();
        var (_, proposal) = await SeedDataAsync(db);
        var service = CreateService(db, Now);

        var request = new ReviewOperatorReplacementRequest(_candidateOperatorId, "Admin manual override for unassigned operator");
        var result = await service.ApproveAsync(_adminId, proposal.Id, request, default);

        Assert.NotNull(result);
        var updated = await db.TemporaryOperatorAssignments.SingleAsync(x => x.Id == proposal.Id);
        Assert.Equal(TemporaryAssignmentStatus.Approved, updated.Status);
        Assert.Equal(_candidateOperatorId, updated.OperatorUserId);
        Assert.Null(updated.AnchorLockerId);
        Assert.Null(updated.DistanceKm);
        Assert.Equal(_adminId, updated.ApprovedByUserId);
        Assert.StartsWith("MANUAL:", updated.SelectionFingerprint ?? string.Empty);
    }

    [Fact]
    public async Task ApproveAsync_ManualSelection_WhenPastStart_ActivatesImmediately()
    {
        await using var db = CreateDbContext();
        var (absence, proposal) = await SeedDataAsync(db);
        absence.StartsAt = Now.AddMinutes(-30);
        await db.SaveChangesAsync();

        var service = CreateService(db, Now);
        var request = new ReviewOperatorReplacementRequest(_candidateOperatorId, "Approve immediate coverage");
        await service.ApproveAsync(_adminId, proposal.Id, request, default);

        var updated = await db.TemporaryOperatorAssignments.SingleAsync(x => x.Id == proposal.Id);
        Assert.Equal(TemporaryAssignmentStatus.Active, updated.Status);
        Assert.NotNull(updated.ActivatedAt);

        // Effective assignments should reflect this manual override
        var effective = await db.EffectiveOperatorAssignments(Now).ToListAsync();
        var current = Assert.Single(effective, x => x.LockerId == _lockerId);
        Assert.Equal(_candidateOperatorId, current.OperatorUserId);
    }

    [Fact]
    public async Task ApproveAsync_ManualSelection_WhenCandidateHasOverlappingAbsence_ThrowsConflictException()
    {
        await using var db = CreateDbContext();
        var (absence, proposal) = await SeedDataAsync(db);

        db.OperatorAbsences.Add(new OperatorAbsence
        {
            Id = Guid.NewGuid(),
            OperatorUserId = _candidateOperatorId,
            CreatedByUserId = _adminId,
            StartsAt = absence.StartsAt.AddMinutes(-10),
            EndsAt = absence.EndsAt.AddMinutes(10),
            Reason = "Also off",
            CreatedAt = Now
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, Now);
        var request = new ReviewOperatorReplacementRequest(_candidateOperatorId, "Try assign overlapping candidate");

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ApproveAsync(_adminId, proposal.Id, request, default));

        Assert.Equal("OPERATOR_ABSENCE_OVERLAP", ex.ErrorCode);
    }

    [Fact]
    public async Task ApproveAsync_ManualSelection_WhenCandidateIsSelf_ThrowsBadRequestException()
    {
        await using var db = CreateDbContext();
        var (_, proposal) = await SeedDataAsync(db);
        var service = CreateService(db, Now);

        var request = new ReviewOperatorReplacementRequest(_originalOperatorId, "Replace with self");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ApproveAsync(_adminId, proposal.Id, request, default));
    }

    [Fact]
    public async Task ApproveAsync_AutomaticApproval_WhenSelectionIsNull_ThrowsConflictExceptionIneligible()
    {
        await using var db = CreateDbContext();
        var (_, proposal) = await SeedDataAsync(db);
        proposal.OperatorUserId = _candidateOperatorId;
        await db.SaveChangesAsync();

        var service = CreateService(db, Now);

        // Request with null OperatorUserId triggers approval of existing proposal suggestion
        var request = new ReviewOperatorReplacementRequest(null, "Approve automatic suggestion");

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ApproveAsync(_adminId, proposal.Id, request, default));

        Assert.Equal("OPERATOR_REPLACEMENT_INELIGIBLE", ex.ErrorCode);
    }

    [Fact]
    public async Task EffectiveOperatorAssignments_WhenAbsenceIsPendingApproval_RetainsOriginalOperatorUntilApproved()
    {
        await using var db = CreateDbContext();
        var (absence, proposal) = await SeedDataAsync(db);
        absence.StartsAt = Now.AddMinutes(-30);
        proposal.Status = TemporaryAssignmentStatus.PendingApproval;
        proposal.ApprovedAt = null;
        proposal.ApprovedByUserId = null;
        proposal.ActivatedAt = null;
        await db.SaveChangesAsync();

        // 1. Unapproved absence: original operator MUST retain their assigned locker
        var effectiveBefore = await db.EffectiveOperatorAssignments(Now).ToListAsync();
        var currentBefore = Assert.Single(effectiveBefore, x => x.LockerId == _lockerId);
        Assert.Equal(_originalOperatorId, currentBefore.OperatorUserId);

        // 2. Admin approves the replacement
        var service = CreateService(db, Now);
        var request = new ReviewOperatorReplacementRequest(_candidateOperatorId, "Approved by admin");
        await service.ApproveAsync(_adminId, proposal.Id, request, default);

        // 3. Approved and activated: candidate operator now effectively manages the locker
        var effectiveAfter = await db.EffectiveOperatorAssignments(Now).ToListAsync();
        var currentAfter = Assert.Single(effectiveAfter, x => x.LockerId == _lockerId);
        Assert.Equal(_candidateOperatorId, currentAfter.OperatorUserId);

        // 4. Admin cancels absence: reverts back to original operator
        await service.CancelAsync(_adminId, nameof(UserRole.Administrator), absence.Id, "Admin cancelled", default);
        var effectiveCancelled = await db.EffectiveOperatorAssignments(Now).ToListAsync();
        var currentCancelled = Assert.Single(effectiveCancelled, x => x.LockerId == _lockerId);
        Assert.Equal(_originalOperatorId, currentCancelled.OperatorUserId);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
