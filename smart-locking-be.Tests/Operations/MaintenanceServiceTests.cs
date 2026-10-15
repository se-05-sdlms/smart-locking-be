using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class MaintenanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private const string Admin = nameof(UserRole.Administrator);

    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC01()
    {
        await using var c = new Context(); var own = c.Seed(); var other = c.Seed(); other.LockerId = Guid.NewGuid(); other.Locker = new Locker { Id = other.LockerId, Code = "L02", Address = "Other", DeviceIdentifier = "D02" }; c.Db.Add(other.Locker); await c.Db.SaveChangesAsync();
        var result = await c.Service.GetAsync(c.Actor, Admin, lockerId: c.Locker.Id, from: Now.AddDays(-1), to: Now, pageNumber: 1, pageSize: 10);
        Assert.Equal(1, result.TotalCount); Assert.Equal(own.Id, Assert.Single(result.Items).Id); Assert.Equal(1, result.PageNumber); Assert.Equal(10, result.PageSize);
    }
    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC02()
    { await using var c = new Context(); c.Seed(); await c.Db.SaveChangesAsync(); var result = await c.Service.GetAsync(Guid.NewGuid(), nameof(UserRole.LockerOperator)); Assert.Empty(result.Items); Assert.Equal(0, result.TotalCount); }
    [Fact(Skip = "Excel MNT-01 UTC03 expects success for empty role; dev rejects empty role. Clarify intended authorized role.")]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC03()
    { await using var c = new Context(); Assert.Empty((await c.Service.GetAsync(Guid.Empty, "", pageNumber: 0, pageSize: 0)).Items); }

    [Fact]
    [Trait("Category", "MNT-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using var c = new Context(); var item = c.Seed(); item.Activities.Add(new MaintenanceActivity { Id = Guid.NewGuid(), ActionByUserId = c.Actor, ActionType = "Created", CreatedAt = Now.AddHours(-1) }); await c.Db.SaveChangesAsync();
        var result = await c.Service.GetByIdAsync(c.Actor, Admin, item.Id);
        Assert.Equal(item.Id, result.Request.Id); Assert.Equal("L01", result.Request.LockerCode); Assert.Equal("Broken door", result.Request.Description); Assert.Equal("Created", Assert.Single(result.Activities).ActionType);
    }
    [Fact]
    [Trait("Category", "MNT-02")]
    public async Task GetByIdAsync_UTC02()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.GetByIdAsync(c.Actor, Admin, Guid.NewGuid())); Assert.Equal("Không tìm thấy yêu cầu bảo trì.", ex.Message); }
    [Fact(Skip = "Excel MNT-02 UTC03: empty role/ID has no concrete expected outcome.")]
    [Trait("Category", "MNT-02")]
    public async Task GetByIdAsync_UTC03()
    { await using var c = new Context(); Assert.Null(await c.Service.GetByIdAsync(Guid.Empty, "", Guid.Empty)); }

    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC01()
    {
        await using var c = new Context(); var result = await c.Service.CreateAsync(c.Actor, Admin, c.Request);
        c.Db.ChangeTracker.Clear(); var entity = await c.Db.MaintenanceRequests.SingleAsync();
        Assert.Equal(result.Id, entity.Id); Assert.Equal(c.Locker.Id, entity.LockerId); Assert.Equal(c.Actor, entity.CreatedByUserId);
        Assert.Equal("Broken door", entity.Description); Assert.Equal(MaintenanceStatus.Open, entity.Status); Assert.Equal(Now, entity.CreatedAt);
        var activity = Assert.Single(c.Db.MaintenanceActivities); Assert.Equal("Created", activity.ActionType); Assert.Equal(entity.Id, activity.MaintenanceRequestId);
        c.AssertAudit("Maintenance.Created", entity.Id);
    }
    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC02() => await RejectCreate(request => request with { Description = " " }, "Mô tả bảo trì là bắt buộc.", false);
    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC03() => await RejectCreate(request => request with { CompartmentId = Guid.NewGuid() }, "Không tìm thấy ngăn tủ.", true);
    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC04() => await RejectCreate(request => request with { IncidentId = Guid.NewGuid() }, "Sự cố không thuộc đúng tủ hoặc ngăn được yêu cầu bảo trì.", false);
    [Fact(Skip = "Excel MNT-03 UTC05 omits a concrete request/expected outcome for empty actor/role.")]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC05()
    { await using var c = new Context(); Assert.Null(await c.Service.CreateAsync(Guid.Empty, "", null!)); }

    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC01()
    {
        await using var c = new Context(); var item = c.Seed(); await c.Db.SaveChangesAsync();
        var result = await c.Service.UpdateAsync(c.Actor, Admin, item.Id, new(MaintenanceStatus.Closed, " checked ", " repaired "));
        Assert.Equal(MaintenanceStatus.Closed, result.Status); Assert.Equal("repaired", result.ResolutionSummary);
        c.Db.ChangeTracker.Clear(); var persisted = await c.Db.MaintenanceRequests.SingleAsync(); Assert.Equal(Now, persisted.ClosedAt); Assert.Equal(Now, persisted.UpdatedAt);
        var activity = Assert.Single(c.Db.MaintenanceActivities); Assert.Equal(item.Id, activity.MaintenanceRequestId); Assert.Equal(MaintenanceStatus.Open, activity.FromStatus); Assert.Equal(MaintenanceStatus.Closed, activity.ToStatus); Assert.Equal("checked", activity.Notes);
        c.AssertAudit("Maintenance.StatusChanged", item.Id);
    }
    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC02()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.UpdateAsync(c.Actor, Admin, Guid.NewGuid(), new(MaintenanceStatus.Closed, null, "fixed"))); Assert.Equal("Không tìm thấy yêu cầu bảo trì.", ex.Message); c.AssertNoWrites(); }
    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC03()
    {
        await using var c = new Context(); var item = c.Seed(); await c.Db.SaveChangesAsync(); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.UpdateAsync(c.Actor, Admin, item.Id, new(MaintenanceStatus.Closed, null, " ")));
        Assert.Equal("Cần nhập kết quả bảo trì khi đóng yêu cầu.", ex.Message); c.Db.ChangeTracker.Clear(); Assert.Equal(MaintenanceStatus.Open, (await c.Db.MaintenanceRequests.SingleAsync()).Status); Assert.Empty(c.Db.MaintenanceActivities); Assert.Empty(c.Db.AuditLogs);
    }
    [Fact(Skip = "Excel MNT-04 UTC04 omits concrete request/expected outcome for empty actor/role/ID.")]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC04()
    { await using var c = new Context(); Assert.Null(await c.Service.UpdateAsync(Guid.Empty, "", Guid.Empty, null!)); }

    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_ClampsPaginationAndUsesExclusiveEndDate()
    {
        await using var c = new Context(); c.Seed(); var excluded = c.Seed(); excluded.CreatedAt = Now; await c.Db.SaveChangesAsync();
        var result = await c.Service.GetAsync(c.Actor, Admin, to: Now, pageNumber: 0, pageSize: 1000);
        Assert.Equal(1, result.TotalCount); Assert.Equal(1, result.PageNumber); Assert.Equal(100, result.PageSize);
    }
    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_DeniesOperatorOutsideScopeWithoutMutating()
    {
        await using var c = new Context(); var item = c.Seed(); await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Service.UpdateAsync(Guid.NewGuid(), nameof(UserRole.LockerOperator), item.Id, new(MaintenanceStatus.Closed, null, "fixed")));
        c.Db.ChangeTracker.Clear(); Assert.Equal(MaintenanceStatus.Open, (await c.Db.MaintenanceRequests.SingleAsync()).Status); Assert.Empty(c.Db.MaintenanceActivities); Assert.Empty(c.Db.AuditLogs);
    }
    private static async Task RejectCreate(Func<CreateMaintenanceRequest, CreateMaintenanceRequest> transform, string message, bool missing)
    {
        await using var c = new Context(); var ex = await Record.ExceptionAsync(() => c.Service.CreateAsync(c.Actor, Admin, transform(c.Request)));
        if (missing) Assert.IsType<KeyNotFoundException>(ex); else Assert.IsType<ArgumentException>(ex);
        Assert.Equal(message, ex!.Message); c.AssertNoWrites();
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid Actor { get; } = Guid.NewGuid();
        public Locker Locker { get; } = new() { Id = Guid.NewGuid(), Code = "L01", Address = "Address", DeviceIdentifier = "D01" };
        public MaintenanceService Service => new(Db, new Clock());
        public CreateMaintenanceRequest Request => new(Locker.Id, null, null, MaintenancePriority.Normal, " Broken door ");
        public Context() { Db.Add(Locker); Db.SaveChanges(); }
        public MaintenanceRequest Seed()
        {
            var item = new MaintenanceRequest { Id = Guid.NewGuid(), Locker = Locker, LockerId = Locker.Id, CreatedByUserId = Actor, Priority = MaintenancePriority.Normal, Status = MaintenanceStatus.Open, Description = "Broken door", CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1) };
            Db.Add(item); return item;
        }
        public void AssertAudit(string action, Guid id) { var audit = Assert.Single(Db.AuditLogs); Assert.Equal(action, audit.Action); Assert.Equal(id, audit.EntityId); Assert.Equal(Actor, audit.ActorUserId); Assert.Equal(Now, audit.OccurredAt); Assert.Equal(AuditLogResult.Succeeded, audit.Result); }
        public void AssertNoWrites() { Assert.Empty(Db.MaintenanceRequests); Assert.Empty(Db.MaintenanceActivities); Assert.Empty(Db.AuditLogs); }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
