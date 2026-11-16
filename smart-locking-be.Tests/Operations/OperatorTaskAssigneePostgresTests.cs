using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;

namespace smart_locking_be.Tests.Operations;

public sealed class OperatorTaskAssigneePostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_task_assignee_{Guid.NewGuid():N}";
    private string connectionString = string.Empty;
    private string adminConnectionString = string.Empty;
    private readonly QueryCounter counter = new();
    private readonly Guid adminId = Guid.NewGuid(), ownerId = Guid.NewGuid(), viewerId = Guid.NewGuid();
    private readonly Guid lockerId = Guid.NewGuid(), anchorId = Guid.NewGuid(), policyId = Guid.NewGuid();
    private readonly Guid assignmentId = Guid.NewGuid(), assignedId = Guid.NewGuid(), unassignedId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        string? configured = Environment.GetEnvironmentVariable("SMART_LOCKING_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        adminConnectionString = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
        await using var db = Context();
        await db.Database.MigrateAsync();
        db.AddRange(
            new User { Id = adminId, Email = "admin@tasks.test", Role = UserRole.Administrator, Status = UserStatus.Active },
            new User { Id = ownerId, PhoneNumber = "0901234567", Email = "owner@tasks.test", Role = UserRole.LockerOperator, Status = UserStatus.Active },
            new ResidentProfile { Id = Guid.NewGuid(), UserId = ownerId, FullName = "Assigned Operator" },
            new User { Id = viewerId, Email = "viewer@tasks.test", Role = UserRole.LockerOperator, Status = UserStatus.Active },
            new Locker { Id = lockerId, Code = "TASK", DeviceIdentifier = "task-device", OperationalStatus = LockerOperationalStatus.Operational },
            new Locker { Id = anchorId, Code = "ANCHOR", DeviceIdentifier = "anchor-device", OperationalStatus = LockerOperationalStatus.Operational },
            new SystemPolicy
            {
                Id = policyId,
                Version = 1,
                Currency = "VND",
                CreatedByUserId = adminId,
                IsActive = true,
                EffectiveFrom = Now.AddDays(-2),
                CompartmentReservationMinutes = 5,
                GuestSessionTimeoutMinutes = 10,
                ManualApprovalTimeoutMinutes = 10,
                OtpMaxAttempts = 5,
                OtpLockoutMinutes = 15,
                MaxStorageHours = 24
            },
            new OperatorAssignment { Id = assignmentId, OperatorUserId = ownerId, LockerId = lockerId, AssignedByUserId = adminId, AssignedAt = Now.AddDays(-1) },
            new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = viewerId, LockerId = anchorId, AssignedByUserId = adminId, AssignedAt = Now.AddDays(-1) },
            Incident(assignedId, ownerId), Incident(unassignedId, null),
            new MaintenanceRequest { Id = Guid.NewGuid(), LockerId = lockerId, CreatedByUserId = ownerId, Description = "Maintenance", Priority = MaintenancePriority.Normal, Status = MaintenanceStatus.Open, CreatedAt = Now },
            new Parcel
            {
                Id = Guid.NewGuid(),
                ParcelCode = "RECOVERY",
                Status = ParcelStatus.Stored,
                MaxStorageUntil = Now.AddHours(-1),
                CreatedAt = Now,
                DeliveryRequest = new DeliveryRequest { Id = Guid.NewGuid(), LockerId = lockerId, SystemPolicyId = policyId, GuestSessionTokenHash = "test-session", CreatedAt = Now }
            });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(adminConnectionString)) return;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task PersistedAssigneeAndFallbacks_AreViewerIndependent_WithoutPerRowQueries()
    {
        await using var db = Context();
        var service = Service(db, Now);
        counter.Count = 0;
        var first = await service.ListAsync(ownerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default);
        Assert.Equal(4, counter.Count);
        Assert.Equal(4, first.Items.Count);
        var assigned = Assert.Single(first.Items, x => x.Id == assignedId);
        Assert.Equal(ownerId, assigned.AssignedOperatorUserId);
        Assert.Equal("Assigned Operator", assigned.AssignedOperatorName);
        Assert.Equal(Now.AddMinutes(60), assigned.DueAt);
        Assert.Equal($"/api/incidents/{assignedId}", assigned.SourcePath);
        Assert.All(first.Items.Where(x => x.Id != assignedId), x =>
        {
            Assert.Null(x.AssignedOperatorUserId);
            Assert.Null(x.AssignedOperatorName);
        });
        Assert.Equal(Now.AddMinutes(15), first.Items.Single(x => x.Id == unassignedId).DueAt);
        Assert.Empty((await service.ListAsync(viewerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default)).Items);
        Assert.Equal(ownerId, (await service.ListAsync(adminId, nameof(UserRole.Administrator), 0, 100, lockerId, default)).Items.Single(x => x.Id == assignedId).AssignedOperatorUserId);

        await db.ResidentProfiles.Where(x => x.UserId == ownerId).ExecuteDeleteAsync();
        // PostgreSQL requires a phone or email login identifier; an ID-only user cannot be persisted.
        foreach (string expected in new[] { "0901234567", "owner@tasks.test" })
        {
            var page = await service.ListAsync(ownerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default);
            Assert.Equal(expected, page.Items.Single(x => x.Id == assignedId).AssignedOperatorName);
            if (expected == "0901234567") await db.Users.Where(x => x.Id == ownerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PhoneNumber, (string?)null));
        }
        db.AddRange(Enumerable.Range(0, 30).Select(_ => Incident(Guid.NewGuid(), ownerId)));
        await db.SaveChangesAsync();
        counter.Count = 0;
        Assert.Equal(34, (await service.ListAsync(ownerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default)).Items.Count);
        Assert.Equal(4, counter.Count);
        var paged = await service.ListAsync(ownerId, nameof(UserRole.LockerOperator), 1, 2, lockerId, default);
        Assert.True(paged.HasMore);
        var all = await service.ListAsync(ownerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default);
        Assert.Equal(all.Items.Skip(1).Take(2).Select(x => x.Id), paged.Items.Select(x => x.Id));
        Assert.Equal(2, paged.Items.Count);
    }

    [PostgresFact]
    public async Task TemporaryCoverageChangesAccessAtBoundary_WithoutChangingIncidentOwner()
    {
        await using var db = Context();
        var absence = new OperatorAbsence { Id = Guid.NewGuid(), OperatorUserId = ownerId, CreatedByUserId = adminId, StartsAt = Now, EndsAt = Now.AddHours(1), Reason = "Coverage", CreatedAt = Now };
        db.AddRange(absence, new TemporaryOperatorAssignment
        {
            Id = Guid.NewGuid(),
            AbsenceId = absence.Id,
            BaseAssignmentId = assignmentId,
            LockerId = lockerId,
            OperatorUserId = viewerId,
            AnchorLockerId = anchorId,
            PolicyId = policyId,
            TargetLocationRevision = (await db.Lockers.SingleAsync(x => x.Id == lockerId)).LocationRevision,
            AnchorLocationRevision = (await db.Lockers.SingleAsync(x => x.Id == anchorId)).LocationRevision,
            Status = TemporaryAssignmentStatus.Active,
            ActivatedAt = Now,
            CreatedAt = Now
        });
        await db.SaveChangesAsync();
        Assert.Empty((await Service(db, Now.AddTicks(-1)).ListAsync(viewerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default)).Items);
        var during = await Service(db, Now).ListAsync(viewerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default);
        Assert.Equal(ownerId, during.Items.Single(x => x.Id == assignedId).AssignedOperatorUserId);
        Assert.Equal("Assigned Operator", during.Items.Single(x => x.Id == assignedId).AssignedOperatorName);
        Assert.Empty((await Service(db, Now).ListAsync(ownerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default)).Items);
        Assert.Empty((await Service(db, Now.AddHours(1)).ListAsync(viewerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default)).Items);
        Assert.Equal(4, (await Service(db, Now.AddHours(1)).ListAsync(ownerId, nameof(UserRole.LockerOperator), 0, 100, lockerId, default)).Items.Count);
    }

    private Incident Incident(Guid id, Guid? owner) => new() { Id = id, LockerId = lockerId, AssignedOperatorUserId = owner, Title = "Locker incident", Type = "Locker", Status = IncidentStatus.Open, CreatedAt = Now };
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options);
    private static OperatorTaskService Service(ApplicationDbContext db, DateTimeOffset now) => new(db, new FixedClock(now), Microsoft.Extensions.Options.Options.Create(new IncidentSlaOptions { Enabled = true }));
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
