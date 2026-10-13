using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Notifications;

public sealed class NotificationRuleServiceTests
{
    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    #region NRL-01: GetAsync
    [Fact]
    [Trait("Category", "NRL-01")]
    public async Task GetAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        var rule = CreateRule(policy.Id, "ParcelStored", NotificationChannel.Push, true);
        dbContext.AddRange(policy, rule);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var result = await service.GetAsync(policy.Id, true, default);

        Assert.Single(result);
        Assert.Equal(rule.Id, result.First().Id);
    }

    [Fact]
    [Trait("Category", "NRL-01")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        var result = await service.GetAsync(Guid.NewGuid(), true, default);
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "NRL-01")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        var result = await service.GetAsync(null, false, default);
        Assert.Empty(result);
    }
    #endregion

    #region NRL-02: GetByIdAsync
    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        var rule = CreateRule(policy.Id, "ParcelStored", NotificationChannel.InApp, true);
        dbContext.AddRange(policy, rule);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var result = await service.GetByIdAsync(rule.Id, default);

        Assert.Equal(rule.Id, result.Id);
        Assert.Equal("ParcelStored", result.EventType);
    }

    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid(), default));

        Assert.Equal("Notification rule not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid(), default));
    }

    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid(), default));
    }

    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid(), default));
    }

    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.Empty, default));
    }
    #endregion

    #region NRL-03: CreateAsync
    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        dbContext.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var response = await service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, "ParcelStored", NotificationChannel.Push, 10, true),
            default);

        Assert.Equal("ParcelStored", response.EventType);
        Assert.Equal(10, response.LeadTimeMinutes);
        Assert.True(response.IsEnabled);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "NotificationRule.Created");
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        dbContext.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, "", NotificationChannel.Push, 10, true),
            default));

        Assert.Equal("Event type must contain between 1 and 100 characters.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        dbContext.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, "ParcelStored", (NotificationChannel)999, 10, true),
            default));

        Assert.Equal("Notification channel is invalid.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        dbContext.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, "ParcelStored", NotificationChannel.Push, -5, true),
            default));

        Assert.Equal("Lead time cannot be negative.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(Guid.NewGuid(), "ParcelStored", NotificationChannel.Push, 10, true),
            default));

        Assert.Equal("System policy not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(
            Guid.Empty, null!, default));
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        var rule = CreateRule(policy.Id, "ParcelStored", NotificationChannel.Push, true);
        dbContext.AddRange(policy, rule);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, "ParcelStored", NotificationChannel.Push, 10, true),
            default));

        Assert.Equal("Notification rule already exists.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(true);
        dbContext.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, "ParcelStored", NotificationChannel.Push, 10, true),
            default));

        Assert.Equal("Rules of an active policy are immutable.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC09()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        dbContext.Add(policy);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        string eventType100 = new string('A', 100);
        var response = await service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, eventType100, NotificationChannel.Push, 10, true),
            default);

        Assert.NotNull(response);
        Assert.Equal(eventType100, response.EventType);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "NotificationRule.Created");
    }
    #endregion

    #region NRL-04: UpdateAsync
    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        var rule = CreateRule(policy.Id, "ParcelStored", NotificationChannel.Push, true);
        dbContext.AddRange(policy, rule);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var response = await service.UpdateAsync(
            Guid.NewGuid(),
            rule.Id,
            new UpdateNotificationRuleRequest(15, false),
            default);

        Assert.Equal(15, response.LeadTimeMinutes);
        Assert.False(response.IsEnabled);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "NotificationRule.Updated");
    }

    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateNotificationRuleRequest(15, false),
            default));

        Assert.Equal("Notification rule not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(true); // active policy -> immutable
        var rule = CreateRule(policy.Id, "ParcelStored", NotificationChannel.Push, true);
        dbContext.AddRange(policy, rule);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            Guid.NewGuid(),
            rule.Id,
            new UpdateNotificationRuleRequest(15, false),
            default));

        Assert.Equal("Rules of an active policy are immutable.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var policy = CreatePolicy(false);
        var rule = CreateRule(policy.Id, "ParcelStored", NotificationChannel.Push, true);
        dbContext.AddRange(policy, rule);
        await dbContext.SaveChangesAsync();

        var service = new NotificationRuleService(dbContext);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            Guid.NewGuid(),
            rule.Id,
            new UpdateNotificationRuleRequest(-10, false),
            default));

        Assert.Equal("Lead time cannot be negative.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new NotificationRuleService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateAsync(
            Guid.Empty, Guid.Empty, null!, default));
    }
    #endregion

    private static SystemPolicy CreatePolicy(bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        Version = 1,
        IsActive = isActive,
        OverdueFeePerHour = 5000,
        Currency = "vnd",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static NotificationRule CreateRule(Guid policyId, string eventType, NotificationChannel channel, bool isEnabled) => new()
    {
        Id = Guid.NewGuid(),
        SystemPolicyId = policyId,
        EventType = eventType,
        Channel = channel,
        LeadTimeMinutes = 10,
        IsEnabled = isEnabled
    };
}
