using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Notifications;

public sealed class NotificationRuleServiceTests
{
    [Fact]
    [Trait("Category", "NRL-01")]
    public async Task GetAsync_UTC01()
    {
        await using var c = new Context(); var rule = c.Seed(); var disabled = c.Seed("Other"); disabled.IsEnabled = false; await c.Db.SaveChangesAsync();
        var result = Assert.Single(await c.Service.GetAsync(c.Policy.Id, true)); Assert.Equal(rule.Id, result.Id); Assert.Equal("ParcelStored", result.EventType); Assert.True(result.IsEnabled); Assert.Empty(c.Db.AuditLogs);
    }
    [Fact]
    [Trait("Category", "NRL-01")]
    public async Task GetAsync_UTC02()
    { await using var c = new Context(); c.Seed(); await c.Db.SaveChangesAsync(); Assert.Empty(await c.Service.GetAsync(Guid.NewGuid(), true)); }
    [Fact]
    [Trait("Category", "NRL-01")]
    public async Task GetAsync_UTC03()
    { await using var c = new Context(); c.Seed(); await c.Db.SaveChangesAsync(); Assert.Empty(await c.Service.GetAsync(Guid.Empty, false)); }
    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using var c = new Context(); var rule = c.Seed(); await c.Db.SaveChangesAsync(); var result = await c.Service.GetByIdAsync(rule.Id);
        Assert.Equal(rule.Id, result.Id); Assert.Equal(c.Policy.Id, result.SystemPolicyId); Assert.Equal("ParcelStored", result.EventType);
        // Excel NRL-02 requires a Created audit during a read; retain this discrepancy visibly.
        Assert.Equal("NotificationRule.Created", Assert.Single(c.Db.AuditLogs).Action);
    }
    [Fact]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC02()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.GetByIdAsync(Guid.NewGuid())); Assert.Equal("Notification rule not found.", ex.Message); Assert.Empty(c.Db.AuditLogs); }
    [Fact(Skip = "Excel NRL-02 UTC03 expects EventType validation on GetByIdAsync(Guid), which has no EventType input; covered under NRL-03 UTC02, not counted twice.")]
    [Trait("Category", "NRL-02")]
    public Task GetByIdAsync_UTC03() => Task.CompletedTask;
    [Fact(Skip = "Excel NRL-02 UTC04 expects Channel validation on a Guid-only read; covered under NRL-03 UTC03.")]
    [Trait("Category", "NRL-02")]
    public Task GetByIdAsync_UTC04() => Task.CompletedTask;
    [Fact(Skip = "Excel NRL-02 UTC05 expects LeadTime validation on a Guid-only read; covered under NRL-03 UTC04.")]
    [Trait("Category", "NRL-02")]
    public Task GetByIdAsync_UTC05() => Task.CompletedTask;
    [Fact(Skip = "Excel NRL-02 UTC06 provides empty ID but no concrete exception contract.")]
    [Trait("Category", "NRL-02")]
    public async Task GetByIdAsync_UTC06()
    { await using var c = new Context(); Assert.Null(await c.Service.GetByIdAsync(Guid.Empty)); }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC01()
    {
        await using var c = new Context(); var response = await c.Service.CreateAsync(c.Actor, c.Request with { EventType = " ParcelStored " });
        c.Db.ChangeTracker.Clear(); var rule = await c.Db.NotificationRules.SingleAsync(); Assert.Equal(response.Id, rule.Id); Assert.Equal("ParcelStored", rule.EventType); Assert.Equal(c.Policy.Id, rule.SystemPolicyId); Assert.Equal(NotificationChannel.Push, rule.Channel); Assert.Equal(10, rule.LeadTimeMinutes); Assert.True(rule.IsEnabled);
        c.AssertAudit("NotificationRule.Created", rule.Id);
    }
    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC02() => await RejectCreate(r => r with { EventType = " " }, "Event type must contain between 1 and 100 characters.");
    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC03() => await RejectCreate(r => r with { Channel = (NotificationChannel)999 }, "Notification channel is invalid.");
    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC04() => await RejectCreate(r => r with { LeadTimeMinutes = -1 }, "Lead time cannot be negative.");
    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC05()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.CreateAsync(c.Actor, c.Request with { SystemPolicyId = Guid.NewGuid() })); Assert.Equal("System policy not found.", ex.Message); c.AssertNoWrites(); }
    [Fact(Skip = "Excel NRL-03 UTC06 gives null request without an expected exception; dev dereferences request.")]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_UTC06()
    { await using var c = new Context(); Assert.Null(await c.Service.CreateAsync(Guid.Empty, null!)); }

    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC01()
    {
        await using var c = new Context(); var rule = c.Seed(); await c.Db.SaveChangesAsync(); var response = await c.Service.UpdateAsync(c.Actor, rule.Id, new(0, false));
        Assert.False(response.IsEnabled); Assert.Equal(0, response.LeadTimeMinutes); c.Db.ChangeTracker.Clear(); var persisted = await c.Db.NotificationRules.SingleAsync(); Assert.False(persisted.IsEnabled); Assert.Equal("ParcelStored", persisted.EventType); Assert.Equal(0, persisted.LeadTimeMinutes); c.AssertAudit("NotificationRule.Updated", rule.Id);
    }
    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC02()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.UpdateAsync(c.Actor, Guid.NewGuid(), new(1, true))); Assert.Equal("Notification rule not found.", ex.Message); c.AssertNoWrites(); }
    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC03()
    {
        await using var c = new Context(); var rule = c.Seed(); c.Policy.IsActive = true; await c.Db.SaveChangesAsync(); var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.UpdateAsync(c.Actor, rule.Id, new(1, false))); Assert.Equal("Rules of an active policy are immutable.", ex.Message); c.AssertUnchanged();
    }
    [Fact]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC04()
    {
        await using var c = new Context(); var rule = c.Seed(); await c.Db.SaveChangesAsync(); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.UpdateAsync(c.Actor, rule.Id, new(-1, false))); Assert.Equal("Lead time cannot be negative.", ex.Message); c.AssertUnchanged();
    }
    [Fact(Skip = "Excel NRL-04 UTC05 gives null request/empty ID without an expected exception.")]
    [Trait("Category", "NRL-04")]
    public async Task UpdateAsync_UTC05()
    { await using var c = new Context(); Assert.Null(await c.Service.UpdateAsync(Guid.Empty, Guid.Empty, null!)); }

    [Fact]
    [Trait("Category", "NRL-03")]
    public async Task CreateAsync_RejectsDuplicateAndActivePolicy()
    {
        await using var c = new Context(); c.Seed(); await c.Db.SaveChangesAsync(); var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.CreateAsync(c.Actor, c.Request)); Assert.Equal("Notification rule already exists.", duplicate.Message);
        c.Policy.IsActive = true; await c.Db.SaveChangesAsync(); var active = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.CreateAsync(c.Actor, c.Request with { EventType = "ParcelPickupReminder" })); Assert.Equal("Rules of an active policy are immutable.", active.Message); c.AssertUnchanged();
    }
    private static async Task RejectCreate(Func<CreateNotificationRuleRequest, CreateNotificationRuleRequest> transform, string message)
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CreateAsync(c.Actor, transform(c.Request))); Assert.Equal(message, ex.Message); c.AssertNoWrites(); }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid Actor { get; } = Guid.NewGuid();
        public SystemPolicy Policy { get; } = new() { Id = Guid.NewGuid(), Currency = "VND", Version = 1 };
        public NotificationRuleService Service => new(Db);
        public CreateNotificationRuleRequest Request => new(Policy.Id, "ParcelStored", NotificationChannel.Push, 10, true);
        public Context() { Db.Add(Policy); Db.SaveChanges(); }
        public NotificationRule Seed(string eventType = "ParcelStored") { var item = new NotificationRule { Id = Guid.NewGuid(), SystemPolicyId = Policy.Id, SystemPolicy = Policy, EventType = eventType, Channel = NotificationChannel.Push, LeadTimeMinutes = 10, IsEnabled = true }; Db.Add(item); return item; }
        public void AssertAudit(string action, Guid id) { var audit = Assert.Single(Db.AuditLogs); Assert.Equal(action, audit.Action); Assert.Equal(id, audit.EntityId); Assert.Equal(Actor, audit.ActorUserId); Assert.Equal(AuditLogResult.Succeeded, audit.Result); }
        public void AssertNoWrites() { Assert.Empty(Db.NotificationRules); Assert.Empty(Db.AuditLogs); }
        public void AssertUnchanged() { Db.ChangeTracker.Clear(); var rule = Assert.Single(Db.NotificationRules); Assert.Equal(10, rule.LeadTimeMinutes); Assert.True(rule.IsEnabled); Assert.Empty(Db.AuditLogs); }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
