using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Dashboards;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Dashboards;

public sealed class AdminServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    #region ADM-01: GetDashboardOverviewAsync
    [Fact]
    [Trait("Category", "ADM-01")]
    public async Task GetDashboardOverviewAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        dbContext.Users.Add(new User { Id = Guid.NewGuid(), Role = UserRole.Resident, Status = UserStatus.Active });
        dbContext.Lockers.Add(new Locker { Id = Guid.NewGuid(), Code = "LK-01", Address = "123 St", OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online });
        await dbContext.SaveChangesAsync();

        var service = new AdminService(dbContext);
        var req = new GetDashboardOverviewRequest(30, 10);
        var overview = await service.GetDashboardOverviewAsync(req);

        Assert.NotNull(overview);
        Assert.NotNull(overview.Statistics);
        Assert.Equal(1, overview.Statistics.TotalLockers);
    }

    [Fact]
    [Trait("Category", "ADM-01")]
    public async Task GetDashboardOverviewAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        var req = new GetDashboardOverviewRequest(30, 10);
        var overview = await service.GetDashboardOverviewAsync(req);

        Assert.NotNull(overview);
        Assert.NotNull(overview.Statistics);
        Assert.Equal(0, overview.Statistics.TotalLockers);
        Assert.Equal(0, overview.Statistics.TotalUsers);
    }

    [Fact]
    [Trait("Category", "ADM-01")]
    public async Task GetDashboardOverviewAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        dbContext.Users.Add(new User { Id = Guid.NewGuid(), Role = UserRole.Administrator, Status = UserStatus.Active });
        await dbContext.SaveChangesAsync();

        var service = new AdminService(dbContext);
        var overview = await service.GetDashboardOverviewAsync(null);

        Assert.NotNull(overview);
        Assert.NotNull(overview.Statistics);
        Assert.Equal(1, overview.Statistics.TotalUsers);
    }
    #endregion
}
