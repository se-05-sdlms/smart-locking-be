using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class OperationalQueryServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "AUDIT")]
    public async Task AuditLogs_FilterAndPaginateNewestFirst()
    {
        await using var db = CreateDbContext();
        var first = Audit("Auth.Login", Now.AddMinutes(-2));
        var second = Audit("User.Updated", Now.AddMinutes(-1));
        db.AuditLogs.AddRange(first, second);
        await db.SaveChangesAsync();

        var result = await new AuditLogService(db).GetAsync(
            "User", null, null, null, null, null, null, pageNumber: 1, pageSize: 1);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("User.Updated", result.Items[0].Action);
    }

    [Fact]
    [Trait("Category", "AUDIT")]
    public async Task AuditLogs_GetByIdReturnsNotFoundForUnknownRecord()
    {
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<NotFoundException>(() => new AuditLogService(db).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION-RULE")]
    public async Task NotificationRules_CreateOnDraftPolicyAndListByEnabledState()
    {
        await using var db = CreateDbContext();
        var policy = new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            IsActive = false,
            EffectiveFrom = Now,
            Currency = "VND"
        };
        db.SystemPolicies.Add(policy);
        await db.SaveChangesAsync();
        var service = new NotificationRuleService(db);

        NotificationRuleResponse response = await service.CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, " ParcelStored ", NotificationChannel.InApp, 5, true));

        Assert.Equal("ParcelStored", response.EventType);
        Assert.Single(await service.GetAsync(policy.Id, true));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION-RULE")]
    public async Task NotificationRules_RejectRulesForActivePolicy()
    {
        await using var db = CreateDbContext();
        var policy = new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            IsActive = true,
            EffectiveFrom = Now,
            Currency = "VND"
        };
        db.SystemPolicies.Add(policy);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => new NotificationRuleService(db).CreateAsync(
            Guid.NewGuid(),
            new CreateNotificationRuleRequest(policy.Id, "ParcelStored", NotificationChannel.InApp, 5, true)));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION-RULE")]
    public async Task NotificationRules_UpdateDraftRuleAndRejectNegativeLeadTime()
    {
        await using var db = CreateDbContext();
        var policy = new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            IsActive = false,
            EffectiveFrom = Now,
            Currency = "VND"
        };
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

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AuditLog Audit(string action, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        Action = action,
        EntityType = "User",
        Result = AuditLogResult.Succeeded,
        Details = "details",
        OccurredAt = occurredAt
    };
}
