using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Incidents;

public sealed class IncidentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private const string Admin = nameof(UserRole.Administrator);
    // INC-01 UTC03 and INC-02 UTC03 are excluded: they require Return/Payment links.

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC01()
    {
        await using var c = new Context();
        var result = await c.Service.CreateGuestIncidentAsync(c.Guest);
        c.Db.ChangeTracker.Clear();
        var item = await c.Db.Incidents.SingleAsync();
        Assert.Equal(result.Id, item.Id);
        Assert.Equal("INC-" + item.Id.ToString("N")[..8].ToUpperInvariant(), result.ReferenceCode);
        Assert.Equal(IncidentStatus.Open, result.Status);
        Assert.Equal(Now, result.CreatedAt);
        Assert.Equal(c.Locker.Id, item.LockerId);
        Assert.Equal(IncidentSource.Guest, item.Source);
        Assert.Equal("Locker", item.Type);
        Assert.Equal("Broken door", item.Title);
        var audit = Assert.Single(c.Db.AuditLogs);
        Assert.Equal("Incident.GuestCreated", audit.Action);
        Assert.Equal(item.Id, audit.EntityId);
        Assert.Equal(AuditLogResult.Succeeded, audit.Result);
        var notification = Assert.Single(c.Db.Notifications);
        Assert.Equal(c.AdminUser.Id, notification.UserId);
        Assert.Equal(item.Id, notification.IncidentId);
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC02()
    {
        await using var c = new Context();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CreateGuestIncidentAsync(c.Guest with { Type = "Invalid" }));
        Assert.StartsWith("Incident type is invalid.", ex.Message);
        c.AssertNoWrites();
    }
    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC04()
    {
        await using var c = new Context();
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.CreateGuestIncidentAsync(c.Guest with { LockerCode = "missing" }));
        Assert.Equal("Locker not found.", ex.Message); c.AssertNoWrites();
    }
    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC05()
    {
        await using var c = new Context();
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.CreateGuestIncidentAsync(c.Guest with { DeliveryRequestId = Guid.NewGuid() }));
        Assert.Equal("Delivery request not found.", ex.Message); c.AssertNoWrites();
    }
    [Fact(Skip = "Excel INC-01 UTC06 has null request and no defined exception/result contract; implementation dereferences request. Clarify expected null handling before claiming coverage.")]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC06()
    {
        await using var c = new Context();
        Assert.Null(await c.Service.CreateGuestIncidentAsync(null!)); c.AssertNoWrites();
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC01()
    {
        await using var c = new Context();
        var response = await c.Service.CreateResidentIncidentAsync(c.Resident.UserId, c.ResidentRequest);
        c.Db.ChangeTracker.Clear();
        var item = await c.Db.Incidents.SingleAsync();
        Assert.Equal(response.Id, item.Id); Assert.Equal(c.Resident.UserId, item.ReporterUserId);
        Assert.Equal(c.Locker.Id, item.LockerId); Assert.Equal(IncidentSource.Resident, item.Source);
        Assert.Equal(IncidentStatus.Open, item.Status); Assert.Equal(Now, item.CreatedAt);
        var action = Assert.Single(c.Db.IncidentActions);
        Assert.Equal("Created", action.ActionType); Assert.Equal(c.Resident.UserId, action.ActionByUserId);
        Assert.Equal(item.Id, action.IncidentId);
        Assert.Equal(c.AdminUser.Id, Assert.Single(c.Db.Notifications).UserId);
        // Excel requires this audit. Keep the assertion even though dev currently omits it.
        var audit = Assert.Single(c.Db.AuditLogs);
        Assert.Equal("Incident.Created", audit.Action); Assert.Equal(item.Id, audit.EntityId);
        Assert.Equal(AuditLogResult.Succeeded, audit.Result);
    }
    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC02()
    {
        await using var c = new Context();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CreateResidentIncidentAsync(c.Resident.UserId, c.ResidentRequest with { Type = "invalid" }));
        Assert.StartsWith("Incident type is invalid.", ex.Message); c.AssertNoWrites();
    }
    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC04()
    {
        await using var c = new Context();
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.CreateResidentIncidentAsync(Guid.NewGuid(), c.ResidentRequest));
        Assert.Equal("Resident profile not found.", ex.Message); c.AssertNoWrites();
    }
    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC05()
    {
        await using var c = new Context();
        var other = new Locker { Id = Guid.NewGuid(), Code = "L02", Address = "Other", DeviceIdentifier = "D02" };
        var request = new DeliveryRequest { Id = Guid.NewGuid(), Locker = other, ResidentProfileId = c.Resident.Id, SystemPolicy = new SystemPolicy { Id = Guid.NewGuid(), Currency = "VND" }, GuestSessionTokenHash = "hash" };
        var parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequest = request, ParcelCode = "P01" };
        c.Db.Add(parcel); await c.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CreateResidentIncidentAsync(c.Resident.UserId, c.ResidentRequest with { ParcelId = parcel.Id }));
        Assert.Equal("Parcel does not belong to the selected locker.", ex.Message); c.AssertNoWrites();
    }
    [Fact(Skip = "Excel INC-02 UTC06: null request has no defined exception contract; dev dereferences it.")]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC06()
    {
        await using var c = new Context(); Assert.Null(await c.Service.CreateResidentIncidentAsync(Guid.Empty, null!));
    }

    [Fact]
    [Trait("Category", "INC-03")]
    public async Task GetResidentIncidentsAsync_UTC01()
    {
        // Excel describes pagination, but dev exposes an unpaged collection. Verify available filtering/order contract.
        await using var c = new Context(); var own = c.SeedIncident(); var other = c.SeedIncident(); other.ReporterUserId = Guid.NewGuid(); await c.Db.SaveChangesAsync();
        var item = Assert.Single(await c.Service.GetResidentIncidentsAsync(c.Resident.UserId));
        Assert.Equal(own.Id, item.Id); Assert.Equal("L01", item.LockerCode); Assert.Equal("Broken door", item.Title);
    }
    [Fact]
    [Trait("Category", "INC-03")]
    public async Task GetResidentIncidentsAsync_UTC02()
    { await using var c = new Context(); c.SeedIncident(); await c.Db.SaveChangesAsync(); Assert.Empty(await c.Service.GetResidentIncidentsAsync(Guid.NewGuid())); }
    [Fact]
    [Trait("Category", "INC-03")]
    public async Task GetResidentIncidentsAsync_UTC03()
    { await using var c = new Context(); c.SeedIncident(); await c.Db.SaveChangesAsync(); Assert.Empty(await c.Service.GetResidentIncidentsAsync(Guid.Empty)); }

    [Fact]
    [Trait("Category", "INC-04")]
    public async Task GetOperationalIncidentsAsync_UTC01()
    {
        await using var c = new Context(); var open = c.SeedIncident(); var resolved = c.SeedIncident(); resolved.Status = IncidentStatus.Resolved; await c.Db.SaveChangesAsync();
        Assert.Equal(open.Id, Assert.Single(await c.Service.GetOperationalIncidentsAsync(c.AdminUser.Id, Admin, IncidentStatus.Open)).Id);
    }
    [Fact]
    [Trait("Category", "INC-04")]
    public async Task GetOperationalIncidentsAsync_UTC02()
    { await using var c = new Context(); c.SeedIncident(); await c.Db.SaveChangesAsync(); Assert.Empty(await c.Service.GetOperationalIncidentsAsync(Guid.NewGuid(), nameof(UserRole.LockerOperator), null)); }
    [Fact(Skip = "Excel INC-04 UTC03 expects success for empty role; dev rejects this role. Pagination arguments from Excel do not exist on dev.")]
    [Trait("Category", "INC-04")]
    public async Task GetOperationalIncidentsAsync_UTC03()
    { await using var c = new Context(); Assert.Empty(await c.Service.GetOperationalIncidentsAsync(Guid.Empty, "", null)); }

    [Fact]
    [Trait("Category", "INC-05")]
    public async Task GetIncidentAsync_UTC01()
    {
        await using var c = new Context(); var item = c.SeedIncident(); await c.Db.SaveChangesAsync();
        var response = await c.Service.GetIncidentAsync(c.Resident.UserId, nameof(UserRole.Resident), item.Id);
        Assert.Equal(item.Id, response.Id); Assert.Equal("L01", response.LockerCode); Assert.Equal("Broken door", response.Title); Assert.Empty(response.Actions);
    }
    [Fact]
    [Trait("Category", "INC-05")]
    public async Task GetIncidentAsync_UTC02()
    {
        await using var c = new Context(); var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.GetIncidentAsync(c.AdminUser.Id, Admin, Guid.NewGuid())); Assert.Equal("Incident not found.", ex.Message);
    }
    [Fact(Skip = "Excel INC-05 UTC03 has empty IDs/role with null exception and no concrete expected outcome.")]
    [Trait("Category", "INC-05")]
    public async Task GetIncidentAsync_UTC03()
    { await using var c = new Context(); Assert.Null(await c.Service.GetIncidentAsync(Guid.Empty, "", Guid.Empty)); }

    [Fact]
    [Trait("Category", "INC-06")]
    public async Task AddActionAsync_UTC01()
    {
        await using var c = new Context(); var item = c.SeedIncident(); await c.Db.SaveChangesAsync();
        var result = await c.Service.AddActionAsync(c.AdminUser.Id, Admin, item.Id, new("  checked door  "));
        Assert.Equal("checked door", Assert.Single(result.Actions).Notes);
        c.Db.ChangeTracker.Clear(); var action = await c.Db.IncidentActions.SingleAsync();
        Assert.Equal(item.Id, action.IncidentId); Assert.Equal(c.AdminUser.Id, action.ActionByUserId); Assert.Equal(Now, action.CreatedAt);
        var audit = Assert.Single(c.Db.AuditLogs); Assert.Equal("Incident.ActionAdded", audit.Action); Assert.Equal(item.Id, audit.EntityId);
    }
    [Fact]
    [Trait("Category", "INC-06")]
    public async Task AddActionAsync_UTC02()
    {
        await using var c = new Context(); var item = c.SeedIncident(); item.Status = IncidentStatus.Resolved; await c.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.AddActionAsync(c.AdminUser.Id, Admin, item.Id, new("note")));
        Assert.Equal("Resolved incidents cannot receive new actions.", ex.Message); Assert.Empty(c.Db.IncidentActions); Assert.Empty(c.Db.AuditLogs);
    }
    [Fact(Skip = "Excel INC-06 UTC03 has null request/empty role with no exception contract.")]
    [Trait("Category", "INC-06")]
    public async Task AddActionAsync_UTC03()
    { await using var c = new Context(); Assert.Null(await c.Service.AddActionAsync(Guid.Empty, "", Guid.Empty, null!)); }

    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC01()
    {
        await using var c = new Context(); var item = c.SeedIncident(); item.Status = IncidentStatus.Investigating; await c.Db.SaveChangesAsync();
        var result = await c.Service.UpdateStatusAsync(c.AdminUser.Id, Admin, item.Id, new(IncidentStatus.Resolved, ResolutionSummary: "  repaired  "));
        Assert.Equal(IncidentStatus.Resolved, result.Status); Assert.Equal("repaired", result.ResolutionSummary); Assert.Equal(Now, result.ResolvedAt);
        c.Db.ChangeTracker.Clear(); Assert.Equal(IncidentStatus.Resolved, (await c.Db.Incidents.SingleAsync()).Status);
        var action = Assert.Single(c.Db.IncidentActions); Assert.Equal(IncidentStatus.Investigating, action.FromStatus); Assert.Equal(IncidentStatus.Resolved, action.ToStatus);
        Assert.Equal(c.Resident.UserId, Assert.Single(c.Db.Notifications).UserId);
        var audit = Assert.Single(c.Db.AuditLogs); Assert.Equal("Incident.StatusChanged", audit.Action); Assert.Equal(item.Id, audit.EntityId);
    }
    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC02() => await RejectStatus(new((IncidentStatus)999), "Incident status is invalid.", argument: true);
    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC03() => await RejectStatus(new(IncidentStatus.Open), "Cannot change incident from Open to Open.", argument: false);
    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC04() => await RejectStatus(new(IncidentStatus.Resolved), "Resolution summary is required when resolving an incident.", argument: true, status: IncidentStatus.Investigating);
    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC05() => await RejectStatus(new(IncidentStatus.Escalated), "Escalation notes are required.", argument: true);
    [Fact(Skip = "Excel INC-07 UTC06 has null request with no defined exception contract.")]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC06()
    { await using var c = new Context(); Assert.Null(await c.Service.UpdateStatusAsync(Guid.Empty, "", Guid.Empty, null!)); }

    [Fact]
    [Trait("Category", "INC-05")]
    public async Task GetIncidentAsync_DeniesUnrelatedResidentAndRevokedOperator()
    {
        await using var c = new Context(); var item = c.SeedIncident(); await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.GetIncidentAsync(Guid.NewGuid(), nameof(UserRole.Resident), item.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.GetIncidentAsync(Guid.NewGuid(), nameof(UserRole.LockerOperator), item.Id));
    }
    private static async Task RejectStatus(UpdateIncidentStatusRequest request, string message, bool argument, IncidentStatus status = IncidentStatus.Open)
    {
        await using var c = new Context(); var item = c.SeedIncident(); item.Status = status; await c.Db.SaveChangesAsync();
        var error = await Record.ExceptionAsync(() => c.Service.UpdateStatusAsync(c.AdminUser.Id, Admin, item.Id, request));
        if (argument) Assert.IsType<ArgumentException>(error); else Assert.IsType<InvalidOperationException>(error);
        Assert.StartsWith(message, error!.Message); c.Db.ChangeTracker.Clear();
        Assert.Equal(status, (await c.Db.Incidents.SingleAsync()).Status); Assert.Empty(c.Db.IncidentActions); Assert.Empty(c.Db.AuditLogs); Assert.Empty(c.Db.Notifications);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Locker Locker { get; } = new() { Id = Guid.NewGuid(), Code = "L01", Address = "Address", DeviceIdentifier = "D01" };
        public User AdminUser { get; } = new() { Id = Guid.NewGuid(), Email = "admin@test.local", Role = UserRole.Administrator, Status = UserStatus.Active, PasswordHash = "hash" };
        public ResidentProfile Resident { get; }
        public IncidentService Service => new(Db, new Clock());
        public CreateGuestIncidentRequest Guest => new(" L01 ", "locker", " Broken door ", " Door stuck ");
        public CreateIncidentRequest ResidentRequest => new("locker", " Broken door ", " Door stuck ", LockerId: Locker.Id);
        public Context()
        {
            var user = new User { Id = Guid.NewGuid(), PhoneNumber = "0912345678", PasswordHash = "hash", Role = UserRole.Resident, Status = UserStatus.Active };
            Resident = new ResidentProfile { Id = Guid.NewGuid(), User = user, UserId = user.Id, FullName = "Resident", RegisteredLockerId = Locker.Id };
            Db.AddRange(Locker, AdminUser, Resident); Db.SaveChanges();
        }
        public Incident SeedIncident()
        {
            var item = new Incident { Id = Guid.NewGuid(), Locker = Locker, LockerId = Locker.Id, ReporterUserId = Resident.UserId, Title = "Broken door", Description = "Door stuck", Type = "Locker", Source = IncidentSource.Resident, Status = IncidentStatus.Open, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1) };
            Db.Add(item); return item;
        }
        public void AssertNoWrites() { Assert.Empty(Db.Incidents); Assert.Empty(Db.IncidentActions); Assert.Empty(Db.Notifications); Assert.Empty(Db.AuditLogs); }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
