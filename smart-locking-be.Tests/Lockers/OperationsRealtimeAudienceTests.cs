using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Tests.Lockers;

public sealed class OperationsRealtimeAudienceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recipients_FollowTemporaryCover_OnlyWithinApprovedAbsence(bool withinAbsence)
    {
        await using var db = CreateDb(); var now = DateTimeOffset.UtcNow;
        var target = new Locker { Id = Guid.NewGuid(), Code = "A", DeviceIdentifier = "ESP-A", LocationRevision = Guid.NewGuid() };
        var anchor = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "ESP-B", LocationRevision = Guid.NewGuid() };
        var original = new User { Id = Guid.NewGuid(), Role = UserRole.LockerOperator, Status = UserStatus.Active };
        var replacement = new User { Id = Guid.NewGuid(), Role = UserRole.LockerOperator, Status = UserStatus.Active };
        var permanent = new OperatorAssignment { Id = Guid.NewGuid(), LockerId = target.Id, OperatorUserId = original.Id, AssignedAt = now.AddDays(-1) };
        var absence = new OperatorAbsence { Id = Guid.NewGuid(), OperatorUserId = original.Id, StartsAt = now.AddMinutes(-1), EndsAt = now.AddMinutes(1) };
        var policy = new SystemPolicy { Id = Guid.NewGuid(), IsActive = true, EffectiveFrom = now.AddHours(-1), Currency = "VND" };
        db.AddRange(target, anchor, original, replacement, permanent, absence, policy,
            new OperatorAssignment { Id = Guid.NewGuid(), LockerId = anchor.Id, OperatorUserId = replacement.Id, AssignedAt = now.AddDays(-1) },
            new TemporaryOperatorAssignment
            {
                Id = Guid.NewGuid(),
                LockerId = target.Id,
                AnchorLockerId = anchor.Id,
                OperatorUserId = replacement.Id,
                BaseAssignmentId = permanent.Id,
                BaseAssignment = permanent,
                AbsenceId = absence.Id,
                Absence = absence,
                PolicyId = policy.Id,
                Status = TemporaryAssignmentStatus.Active,
                ActivatedAt = now.AddMinutes(-1),
                TargetLocationRevision = target.LocationRevision,
                AnchorLocationRevision = anchor.LocationRevision
            });
        await db.SaveChangesAsync();
        var recipients = await new OperationsRealtimeAudience(db).GetRecipientIdsAsync(target.Id, withinAbsence ? now : now.AddMinutes(2));
        Assert.Equal(withinAbsence ? replacement.Id : original.Id, Assert.Single(recipients));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recipients_RespectLockerScopeAccountStatusAndAbsence(bool absent)
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        Guid locker = Guid.NewGuid(), other = Guid.NewGuid();
        User AddUser(UserRole role = UserRole.LockerOperator, UserStatus status = UserStatus.Active)
        {
            var user = new User { Id = Guid.NewGuid(), Role = role, Status = status };
            db.Add(user); return user;
        }
        void Assign(User user, Guid lockerId, DateTimeOffset? revoked = null, DateTimeOffset? starts = null) =>
            db.Add(new OperatorAssignment
            {
                Id = Guid.NewGuid(),
                OperatorUserId = user.Id,
                LockerId = lockerId,
                AssignedAt = starts ?? now.AddDays(-1),
                RevokedAt = revoked
            });
        var admin = AddUser(UserRole.Administrator);
        AddUser(UserRole.Administrator, UserStatus.Disabled);
        var assigned = AddUser(); Assign(assigned, locker);
        Assign(AddUser(), other);
        Assign(AddUser(), locker, revoked: now.AddMinutes(-1));
        Assign(AddUser(status: UserStatus.Disabled), locker);
        Assign(AddUser(), locker, starts: now.AddMinutes(1));
        Assign(AddUser(UserRole.Resident), locker);
        if (absent) db.Add(new OperatorAbsence
        {
            Id = Guid.NewGuid(),
            OperatorUserId = assigned.Id,
            StartsAt = now.AddMinutes(-1),
            EndsAt = now.AddMinutes(1)
        });
        await db.SaveChangesAsync();

        var recipients = await new OperationsRealtimeAudience(db).GetRecipientIdsAsync(locker, now);

        Assert.Contains(admin.Id, recipients);
        Assert.Equal(absent ? 1 : 2, recipients.Count);
        Assert.Equal(!absent, recipients.Contains(assigned.Id));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task DeviceLookup_IsReadOnlyAndDoesNotInventUnknownDevice()
    {
        await using var db = CreateDb();
        var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "ESP-B" };
        db.Add(locker); await db.SaveChangesAsync();
        var reader = new OperationsRealtimeAudience(db);
        Assert.Equal("ESP-B", await reader.GetDeviceIdentifierAsync(locker.Id));
        Assert.Null(await reader.GetDeviceIdentifierAsync(Guid.NewGuid()));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
