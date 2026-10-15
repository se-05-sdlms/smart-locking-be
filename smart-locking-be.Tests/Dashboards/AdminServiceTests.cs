using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Dashboards;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Dashboards;

public sealed class AdminServiceTests
{
    [Fact]
    [Trait("Category", "ADM-01")]
    public async Task GetDashboardOverviewAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new AdminService(db);
        var active = Locker();
        var offline = Locker("L02");
        offline.ConnectionStatus = LockerConnectionStatus.Offline;
        offline.OperationalStatus = LockerOperationalStatus.OutOfService;
        db.AddRange(active, offline, Locker("L03"), Compartment(active), User(UserRole.Resident), User(UserRole.Resident), User(UserRole.LockerOperator));
        await db.SaveChangesAsync();
        var result = await service.GetDashboardOverviewAsync(new GetDashboardOverviewRequest(3, 1));
        Assert.Equal(3, result.Statistics.TotalUsers);
        Assert.Equal(3, result.Statistics.TotalLockers);
        Assert.Equal(3, result.RecentParcelsTrend.Count);
        Assert.Equal(2, result.Statistics.ResidentCount);
        Assert.Equal(1, result.Statistics.OperatorCount);
        Assert.Equal(2, result.LockerStatus.Operational);
        Assert.Equal(1, result.LockerStatus.Maintenance);
        Assert.Equal(1, result.LockerStatus.Offline);
        Assert.Equal(offline.Id, Assert.Single(result.AttentionLockers).Id);
        Assert.Equal(1, result.CompartmentStatus.Available);
        Assert.Empty(result.RecentAuditLogs);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "ADM-01")]
    public async Task GetDashboardOverviewAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new AdminService(db);
        var result = await service.GetDashboardOverviewAsync(new GetDashboardOverviewRequest(3, 1));
        Assert.Equal(0, result.Statistics.TotalUsers);
        Assert.Equal(0, result.Statistics.TotalLockers);
        Assert.Equal(3, result.RecentParcelsTrend.Count);
        Assert.Empty(result.RecentLockers);
        Assert.Empty(result.AttentionLockers);
        Assert.Equal(0m, result.Statistics.TotalOverdueFeeAmountLast30Days);
        Assert.Empty(result.RecentAuditLogs);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "ADM-01")]
    public async Task GetDashboardOverviewAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new AdminService(db);
        var active = Locker();
        var offline = Locker("L02");
        offline.ConnectionStatus = LockerConnectionStatus.Offline;
        offline.OperationalStatus = LockerOperationalStatus.OutOfService;
        db.AddRange(active, offline, Locker("L03"), Compartment(active), User(UserRole.Resident), User(UserRole.Resident), User(UserRole.LockerOperator));
        await db.SaveChangesAsync();
        var result = await service.GetDashboardOverviewAsync(null);
        Assert.Equal(3, result.Statistics.TotalUsers);
        Assert.Equal(3, result.Statistics.TotalLockers);
        Assert.Equal(7, result.RecentParcelsTrend.Count);
        Assert.Equal(2, result.Statistics.ResidentCount);
        Assert.Equal(1, result.Statistics.OperatorCount);
        Assert.Equal(2, result.LockerStatus.Operational);
        Assert.Equal(1, result.LockerStatus.Maintenance);
        Assert.Equal(1, result.LockerStatus.Offline);
        Assert.Equal(offline.Id, Assert.Single(result.AttentionLockers).Id);
        Assert.Equal(1, result.CompartmentStatus.Available);
        Assert.Empty(result.RecentAuditLogs);
        Assert.Empty(db.AuditLogs);
    }


    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static User User(UserRole role = UserRole.Resident) => new() { Id = Guid.NewGuid(), PasswordHash = "hash", Email = $"{Guid.NewGuid():N}@example.com", PhoneNumber = "0912345678", Role = role, Status = UserStatus.Active, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static Locker Locker(string code = "L01") => new() { Id = Guid.NewGuid(), Code = code, Address = "Address", RecoveryAddress = "Recovery", DeviceIdentifier = "DEVICE-" + code, OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static LockerCompartment Compartment(Locker locker, string code = "C01") => new() { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = code, HardwareCode = "HW-" + code, HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
}
