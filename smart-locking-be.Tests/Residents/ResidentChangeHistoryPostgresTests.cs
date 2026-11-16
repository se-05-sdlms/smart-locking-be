using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Tests.Auth;

namespace smart_locking_be.Tests.Residents;

public sealed class ResidentChangeHistoryPostgresTests : IAsyncLifetime
{
    private readonly string databaseName = $"smart_locking_resident_history_{Guid.NewGuid():N}";
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid residentId = Guid.NewGuid();
    private readonly Guid targetId = Guid.NewGuid();
    private string connectionString = string.Empty;
    private string adminConnectionString = string.Empty;

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
            new User { Id = adminId, Email = "review@example.test", Role = UserRole.Administrator, Status = UserStatus.Active },
            new User { Id = residentId, PhoneNumber = "0901234567", Role = UserRole.Resident, Status = UserStatus.Active },
            new ResidentProfile { Id = Guid.NewGuid(), UserId = residentId },
            new Locker { Id = targetId, Code = "HISTORY", DeviceIdentifier = "history-device", OperationalStatus = LockerOperationalStatus.Operational });
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
    public async Task ApproveFromNullLocker_PersistsIdentityHistoryAuditAndNotification_AndRetryIsIdempotent()
    {
        Guid id = await Submit();
        await using (var db = Context())
        {
            var result = await Service(db).ReviewAsync(adminId, id, new(true, "Approved"), default);
            Assert.Equal("Applied", result.Status);
            Assert.Equal(2, result.History.Count);
        }
        await using (var db = Context())
            Assert.Equal("Applied", (await Service(db).ReviewAsync(adminId, id, new(true, "Retry"), default)).Status);
        await using var read = Context();
        Assert.Equal(targetId, (await read.ResidentProfiles.SingleAsync()).RegisteredLockerId);
        Assert.Equal(2, await read.Set<ResidentAccountChangeEvent>().CountAsync(e => e.ResidentAccountChangeId == id));
        Assert.Single(await read.AuditLogs.Where(e => e.EntityId == id && e.Action == "ResidentAccountChange.Applied").ToListAsync());
        Assert.Single(await read.Notifications.Where(n => n.UserId == residentId).ToListAsync());
    }

    [PostgresFact]
    public async Task RejectExistingRequest_PersistsHistoryWithoutChangingLocker()
    {
        Guid id = await Submit();
        await using (var db = Context())
        {
            var result = await Service(db).ReviewAsync(adminId, id, new(false, "Rejected"), default);
            Assert.Equal("Rejected", result.Status);
            Assert.Equal(2, result.History.Count);
        }
        await using var read = Context();
        Assert.Null((await read.ResidentProfiles.SingleAsync()).RegisteredLockerId);
        Assert.Single(await read.Set<ResidentAccountChangeEvent>().Where(e => e.ResidentAccountChangeId == id && e.Action == "Rejected").ToListAsync());
    }

    [PostgresFact]
    public async Task TargetBecomesUnavailable_PersistsBlockedHistoryAndLeavesRequestSubmitted()
    {
        Guid id = await Submit();
        await using (var db = Context())
        {
            await db.Lockers.Where(l => l.Id == targetId).ExecuteUpdateAsync(s => s.SetProperty(l => l.OperationalStatus, LockerOperationalStatus.OutOfService));
            var result = await Service(db).ReviewAsync(adminId, id, new(true, "Review"), default);
            Assert.Equal("Submitted", result.Status);
            Assert.NotEmpty(result.Blockers);
            Assert.Equal(2, result.History.Count);
        }
        await using var read = Context();
        Assert.Null((await read.ResidentProfiles.SingleAsync()).RegisteredLockerId);
        Assert.Single(await read.Set<ResidentAccountChangeEvent>().Where(e => e.ResidentAccountChangeId == id && e.Action == "ApprovalBlocked").ToListAsync());
        Assert.Empty(await read.Notifications.ToListAsync());
    }

    [PostgresFact]
    public async Task CancelExistingRequest_PersistsHistoryAndRetryDoesNotDuplicateIt()
    {
        Guid id = await Submit();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var db = Context();
            Assert.Equal("Cancelled", (await Service(db).CancelAsync(residentId, id, default)).Status);
        }
        await using var read = Context();
        Assert.Equal(2, await read.Set<ResidentAccountChangeEvent>().CountAsync(e => e.ResidentAccountChangeId == id));
        Assert.Null((await read.ResidentProfiles.SingleAsync()).RegisteredLockerId);
    }

    private async Task<Guid> Submit()
    {
        await using var db = Context();
        var result = await Service(db).CreateLockerAsync(residentId, new CreateLockerChangeRequest(targetId, "Move locker"), default);
        Assert.Null(result.OriginalLockerId);
        Assert.Single(result.History);
        return result.Id;
    }

    [PostgresFact]
    public async Task ConcurrentApprovals_ApplyOnceAndDoNotDuplicateHistoryOrNotification()
    {
        Guid id = await Submit();
        async Task Review()
        {
            await using var db = Context();
            Assert.Equal("Applied", (await Service(db).ReviewAsync(adminId, id, new(true, "Approve"), default)).Status);
        }
        await Task.WhenAll(Review(), Review()).WaitAsync(TimeSpan.FromSeconds(15));
        await using var read = Context();
        Assert.Equal(targetId, (await read.ResidentProfiles.SingleAsync()).RegisteredLockerId);
        Assert.Equal(2, await read.Set<ResidentAccountChangeEvent>().CountAsync(e => e.ResidentAccountChangeId == id));
        Assert.Single(await read.AuditLogs.Where(e => e.EntityId == id && e.Action == "ResidentAccountChange.Applied").ToListAsync());
        Assert.Single(await read.Notifications.Where(n => n.UserId == residentId).ToListAsync());
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    private static ResidentAccountChangeService Service(ApplicationDbContext db) => new(db, null!, null!, TimeProvider.System);
}

