using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Forecast;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class AdminForecastServiceTests
{
    [Fact]
    public async Task EmptyDatabase_ReturnsForecastWindowWithoutRiskLockers()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var response = await new AdminForecastService(db).GetForecastAsync();

        Assert.Equal(7, response.HorizonDays);
        Assert.Equal(7, response.DailyForecast.Count);
        Assert.Empty(response.AtRiskLockers);
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 2, 2)]
    [InlineData(2026, 3, 3)]
    [InlineData(2026, 4, 4)]
    [InlineData(2026, 5, 5)]
    [InlineData(2026, 6, 6)]
    [InlineData(2026, 7, 7)]
    [InlineData(2026, 8, 8)]
    [InlineData(2026, 9, 9)]
    [InlineData(2026, 10, 10)]
    [InlineData(2026, 11, 11)]
    [InlineData(2026, 12, 12)]
    public void DemandEventDate_UsesSaleCalendar(int year, int month, int day)
    {
        Assert.True(ForecastCalendar.IsDemandEventWindow(new DateOnly(year, month, day)));
        Assert.Equal(1.15, ForecastCalendar.GetDemandMultiplier(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(2026, 1, 3)]
    [InlineData(2026, 2, 4)]
    [InlineData(2026, 3, 5)]
    [InlineData(2026, 4, 6)]
    [InlineData(2026, 5, 7)]
    [InlineData(2026, 6, 8)]
    [InlineData(2026, 7, 9)]
    [InlineData(2026, 8, 10)]
    [InlineData(2026, 9, 11)]
    [InlineData(2026, 10, 12)]
    [InlineData(2026, 11, 13)]
    [InlineData(2026, 12, 14)]
    public void DaysAfterDemandEvent_UsesArrivalWindowMultiplier(int year, int month, int day)
    {
        Assert.True(ForecastCalendar.IsDemandEventWindow(new DateOnly(year, month, day)));
        Assert.Equal(1.35, ForecastCalendar.GetDemandMultiplier(new DateOnly(year, month, day)));
    }

    [Fact]
    public void OrdinaryDate_UsesBaselineMultiplier()
    {
        var date = new DateOnly(2026, 6, 18);

        Assert.False(ForecastCalendar.IsDemandEventWindow(date));
        Assert.Equal(1.0, ForecastCalendar.GetDemandMultiplier(date));
    }
}

