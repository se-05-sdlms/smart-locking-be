using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Subscriptions;

public sealed class SubscriptionPlanServiceTests
{
    [Fact]
    public async Task CreateAndUpdate_ValidateCodesAndFilterInactivePlans()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new SubscriptionPlanService(db, TimeProvider.System);
        var plan = await service.CreateAsync(new(" BASIC ", "Basic", null, 50000, 30, 2));
        Assert.Equal("BASIC", plan.Code);
        Assert.True(plan.IsActive);
        Assert.Equal(50000, (await db.SubscriptionPlans.SingleAsync()).Price);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new("BASIC", "Other", null, 100, 1, 1)));
        await service.UpdateAsync(plan.Id, new("Renamed", null, 75000, 60, 3, false));
        Assert.Empty(await service.GetPlansAsync());
        Assert.Equal("Renamed", Assert.Single(await service.GetPlansAsync(false)).Name);
    }

    [Theory]
    [InlineData(-1, 30, 2)]
    [InlineData(100, 0, 2)]
    [InlineData(100, 30, 0)]
    public async Task Create_InvalidValues_Rejects(decimal price, int days, int capacity)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new SubscriptionPlanService(db, TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new("BASIC", "Basic", null, price, days, capacity)));
        Assert.Empty(db.SubscriptionPlans);
    }
}
