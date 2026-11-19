using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class SubscriptionPlanServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task GetPlansAsync_FiltersInactiveAndSortsByCode()
    {
        await using var db = CreateDbContext();
        db.SubscriptionPlans.AddRange(Plan("BASIC", true), Plan("ACTIVE", true), Plan("OLD", false));
        await db.SaveChangesAsync();

        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        var active = await service.GetPlansAsync();
        var all = await service.GetPlansAsync(false);

        Assert.Equal(new[] { "ACTIVE", "BASIC" }, active.Select(item => item.Code));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task CreateAsync_TrimsCodeAndPersistsPlan()
    {
        await using var db = CreateDbContext();
        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        SubscriptionPlanResponse response = await service.CreateAsync(
            new CreateSubscriptionPlanRequest(" basic ", "Basic", "Description", 100, 30, 5), default);

        Assert.Equal("basic", response.Code);
        Assert.Equal(Now, db.SubscriptionPlans.Single().CreatedAt);
    }

    [Theory]
    [InlineData("", "Name", 100, 30, 5)]
    [InlineData("CODE", "", 100, 30, 5)]
    [InlineData("CODE", "Name", -1, 30, 5)]
    [InlineData("CODE", "Name", 100, 0, 5)]
    [InlineData("CODE", "Name", 100, 30, 0)]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task CreateAsync_RejectsInvalidData(string code, string name, decimal price, int days, int capacity)
    {
        await using var db = CreateDbContext();
        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(
            new CreateSubscriptionPlanRequest(code, name, null, price, days, capacity), default));
    }

    [Fact]
    [Trait("Category", "SUBSCRIPTION-PLAN")]
    public async Task UpdateAsync_ReturnsNotFoundForMissingPlan()
    {
        await using var db = CreateDbContext();
        var service = new SubscriptionPlanService(db, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(
            Guid.NewGuid(), new UpdateSubscriptionPlanRequest("Basic", null, 100, 30, 5, true), default));
    }

    private static ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SubscriptionPlan Plan(string code, bool active) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Price = 100,
        DurationDays = 30,
        MaxActiveParcels = 5,
        IsActive = active,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
