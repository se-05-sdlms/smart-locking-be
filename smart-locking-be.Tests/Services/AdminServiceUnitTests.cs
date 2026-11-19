using Microsoft.EntityFrameworkCore;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class AdminServiceUnitTests
{
    [Fact]
    [Trait("Category", "ADMIN")]
    public async Task Dashboard_EmptyDatabaseReturnsZeroStatisticsAndDefaultTrend()
    {
        await using var db = CreateDbContext();

        var response = await new AdminService(db).GetDashboardOverviewAsync();

        Assert.Equal(0, response.Statistics.TotalUsers);
        Assert.Equal(0, response.Statistics.TotalLockers);
        Assert.Equal(0, response.Statistics.TotalCompartments);
        Assert.Equal(7, response.RecentParcelsTrend.Count);
        Assert.Empty(response.AttentionLockers);
    }

    [Fact]
    [Trait("Category", "ADMIN")]
    public async Task Statistics_EmptyDatabaseReturnsZeroValues()
    {
        await using var db = CreateDbContext();

        var response = await new AdminService(db).GetSystemStatisticsAsync();

        Assert.Equal(0, response.TotalUsers);
        Assert.Equal(0, response.TotalLockers);
        Assert.Equal(0, response.StoredParcelsCount);
        Assert.Equal(0m, response.TotalOverdueFeeAmountLast30Days);
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
