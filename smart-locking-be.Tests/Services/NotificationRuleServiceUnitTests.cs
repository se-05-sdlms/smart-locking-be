using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class NotificationRuleServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "NOTIFICATION-RULE")]
    public async Task CreateAsync_CreatesRuleOnDraftPolicyAndListFiltersEnabledState()
    {
        await using var db = CreateDbContext();
        var policy = Policy(false);
        db.SystemPolicies.Add(policy);
        await db.SaveChangesAsync();
        var service = new NotificationRuleService(db);

        NotificationRuleResponse response = await service.CreateAsync(
            Guid.NewGuid(), new CreateNotificationRuleRequest(policy.Id, " ParcelStored ", NotificationChannel.InApp, 5, true));

        Assert.Equal("ParcelStored", response.EventType);
        Assert.Single(await service.GetAsync(policy.Id, true));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION-RULE")]
    public async Task CreateAsync_RejectsRuleForActivePolicy()
    {
        await using var db = CreateDbContext();
        var policy = Policy(true);
        db.SystemPolicies.Add(policy);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => new NotificationRuleService(db).CreateAsync(
            Guid.NewGuid(), new CreateNotificationRuleRequest(policy.Id, "ParcelStored", NotificationChannel.InApp, 5, true)));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION-RULE")]
    public async Task UpdateAsync_UpdatesDraftRuleAndRejectsNegativeLeadTime()
    {
        await using var db = CreateDbContext();
        var policy = Policy(false);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            SystemPolicyId = policy.Id,
            SystemPolicy = policy,
            EventType = "ParcelStored",
            Channel = NotificationChannel.InApp,
            LeadTimeMinutes = 5,
            IsEnabled = true
        };
        db.SystemPolicies.Add(policy);
        db.NotificationRules.Add(rule);
        await db.SaveChangesAsync();
        var service = new NotificationRuleService(db);

        NotificationRuleResponse updated = await service.UpdateAsync(
            Guid.NewGuid(), rule.Id, new UpdateNotificationRuleRequest(10, false));

        Assert.Equal(10, updated.LeadTimeMinutes);
        Assert.False(updated.IsEnabled);
        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(
            Guid.NewGuid(), rule.Id, new UpdateNotificationRuleRequest(-1, true)));
    }

    private static ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SystemPolicy Policy(bool active) => new()
    {
        Id = Guid.NewGuid(),
        Version = 1,
        IsActive = active,
        EffectiveFrom = Now,
        Currency = "VND"
    };
}
