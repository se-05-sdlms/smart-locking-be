using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.API;

public sealed class OperationalPhonePrivacyPostgresTests : IAsyncLifetime
{
    private const string Phone = "0901234001";
    private const string Masked = "090****001";
    private readonly string databaseName = $"smart_locking_privacy_test_{Guid.NewGuid():N}";
    private readonly Guid adminId = Guid.NewGuid(), operatorId = Guid.NewGuid();
    private readonly Guid lockerId = Guid.NewGuid(), otherLockerId = Guid.NewGuid();
    private readonly Guid requestId = Guid.NewGuid(), eventId = Guid.NewGuid();
    private readonly Guid auditId = Guid.NewGuid(), incidentId = Guid.NewGuid();
    private string adminConnectionString = string.Empty, connectionString = string.Empty;

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
        var now = DateTimeOffset.UtcNow;
        var adminUser = new User { Id = adminId, Email = "privacy@example.test", Role = UserRole.Administrator, Status = UserStatus.Active };
        var policy = new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            CreatedByUser = adminUser,
            Currency = "VND",
            CompartmentReservationMinutes = 5,
            GuestSessionTimeoutMinutes = 10,
            ManualApprovalTimeoutMinutes = 10,
            OtpMaxAttempts = 5,
            OtpLockoutMinutes = 15,
            MaxStorageHours = 24
        };
        var compartment = new LockerCompartment { Id = Guid.NewGuid(), LockerId = lockerId, Code = Phone, HardwareCode = Phone, HardwareChannel = 1 };
        db.AddRange(adminUser, policy,
            new User { Id = operatorId, PhoneNumber = Phone, Role = UserRole.LockerOperator, Status = UserStatus.Active },
            new Locker { Id = lockerId, Code = "PRIVACY", DeviceIdentifier = "privacy", Address = "Call " + Phone },
            new Locker { Id = otherLockerId, Code = "OUTSIDE", DeviceIdentifier = "outside" },
            new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = lockerId, AssignedByUserId = adminId, AssignedAt = now.AddDays(-1) },
            compartment,
            new DeliveryRequest { Id = requestId, LockerId = lockerId, SystemPolicy = policy, AllocatedCompartment = compartment, RecipientPhoneSnapshot = Phone, UpdatedAt = now, CreatedAt = now },
            new LockerEvent { Id = eventId, LockerId = lockerId, Details = "Call +84 90 123 4001", Reason = Phone, PreviousValue = Phone, NewValue = Phone, OccurredAt = now, ReceivedAt = now },
            new Incident { Id = incidentId, LockerId = lockerId, Title = "Call " + Phone, Description = Phone, ResolutionSummary = Phone, UpdatedAt = now, CreatedAt = now },
            new AuditLog { Id = auditId, ActorUserId = operatorId, Action = "Auth.LoginSucceeded", Details = "Historic phone=" + Phone, IpAddress = "127.0.0.1", OccurredAt = now });
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

    [ReportPostgresFact]
    public async Task HistoricAuditListAndDetail_MaskActorAndDetails_KeepSearchMetadataAndStoredEvidence()
    {
        await using var db = Context();
        var service = new AuditLogService(db);
        var page = await service.GetAsync(Phone, operatorId, "Auth.LoginSucceeded", null, null, null, null);
        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(Masked, item.Actor);
        Assert.Equal("Historic phone=" + Masked, item.Details);
        Assert.Equal(operatorId, item.ActorUserId);
        Assert.Equal("127.0.0.1", item.IpAddress);
        Assert.Equal(item, await service.GetByIdAsync(auditId));
        Assert.Equal("Historic phone=" + Phone, (await db.AuditLogs.AsNoTracking().SingleAsync()).Details);
        Assert.Equal(Phone, (await db.Users.SingleAsync(u => u.Id == operatorId)).PhoneNumber);
    }

    [ReportPostgresFact]
    public async Task DeliveryPhoneListAndDetail_AreMaskedForAdminAndAssignedOperator_WithoutChangingStoredPhone()
    {
        await using var db = Context();
        var service = new OperationalRecordService(db);
        foreach (var (userId, role) in new[] { (adminId, "Administrator"), (operatorId, "LockerOperator") })
        {
            var page = await service.SearchAsync(userId, role, Phone, lockerId, "DeliveryRequest", null, null);
            var item = Assert.Single(page.Items);
            Assert.Equal(requestId, item.Id);
            Assert.Equal(requestId.ToString(), item.Code);
            Assert.Equal(Masked, item.Summary);
            var detail = await service.GetDetailAsync(userId, role, "delivery-request", requestId);
            Assert.Equal(Masked, detail.Details["recipientPhone"]);
            Assert.Equal(Phone, detail.Details["compartmentCode"]); // Identifier, not a contact field.
            Assert.Equal(lockerId, detail.LockerId);
            var compartments = await service.SearchAsync(userId, role, null, lockerId, "LockerCompartment", null, null);
            var compartment = Assert.Single(compartments.Items);
            Assert.Equal(Phone, compartment.Code);
            Assert.Equal(Phone, compartment.Summary);
        }
        Assert.Equal(Phone, (await db.DeliveryRequests.AsNoTracking().SingleAsync()).RecipientPhoneSnapshot);
    }

    [ReportPostgresFact]
    public async Task FreeTextListAndDetail_MaskPhoneInHistoricEventIncidentAndLockerAddress()
    {
        await using var db = Context();
        var service = new OperationalRecordService(db);
        var page = await service.SearchAsync(operatorId, "LockerOperator", null, lockerId, null, null, null);
        Assert.Equal("Call " + Masked, Assert.Single(page.Items, i => i.Kind == "Incident").Summary);
        Assert.Equal("Call " + Masked, Assert.Single(page.Items, i => i.Kind == "LockerEvent").Summary);
        Assert.Equal("Call " + Masked, Assert.Single(page.Items, i => i.Kind == "Locker").Summary);
        var incident = await service.GetDetailAsync(operatorId, "LockerOperator", "incident", incidentId);
        Assert.Equal("Call " + Masked, incident.Details["title"]);
        Assert.Equal(Masked, incident.Details["description"]);
        Assert.Equal(Masked, incident.Details["resolution"]);
        var entry = await service.GetDetailAsync(operatorId, "LockerOperator", "locker-event", eventId);
        foreach (string key in new[] { "previousValue", "newValue", "reason" })
            Assert.Equal(Masked, entry.Details[key]);
        Assert.Equal("Call " + Masked, entry.Details["details"]);
        Assert.Equal("Call +84 90 123 4001", (await db.LockerEvents.AsNoTracking().SingleAsync()).Details);
    }

    [ReportPostgresFact]
    public async Task ScopeDenialsAndRevokedAssignment_DoNotExposePhoneOrRecord()
    {
        await using var db = Context();
        var service = new OperationalRecordService(db);
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => service.SearchAsync(operatorId, "LockerOperator", Phone, otherLockerId, null, null, null));
        Assert.DoesNotContain(Phone, error.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SearchAsync(operatorId, "Resident", Phone, null, null, null, null));
        await db.OperatorAssignments.ExecuteUpdateAsync(u => u.SetProperty(a => a.RevokedAt, DateTimeOffset.UtcNow));
        Assert.Empty((await service.SearchAsync(operatorId, "LockerOperator", Phone, null, null, null, null)).Items);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => service.GetDetailAsync(operatorId, "LockerOperator", "delivery-request", requestId));
        Assert.DoesNotContain(Phone, missing.Message);
    }

    [ReportPostgresFact]
    public async Task DisabledOperatorAndFutureAssignment_HaveNoEffectiveScope()
    {
        await using var db = Context();
        var service = new OperationalRecordService(db);
        await db.Users.Where(u => u.Id == operatorId).ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, UserStatus.Disabled));
        Assert.Empty((await service.SearchAsync(operatorId, "LockerOperator", Phone, null, null, null, null)).Items);
        await db.Users.Where(u => u.Id == operatorId).ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, UserStatus.Active));
        await db.OperatorAssignments.ExecuteUpdateAsync(u => u.SetProperty(i => i.AssignedAt, DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Empty((await service.SearchAsync(operatorId, "LockerOperator", Phone, null, null, null, null)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDetailAsync(operatorId, "LockerOperator", "incident", incidentId));
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
}
