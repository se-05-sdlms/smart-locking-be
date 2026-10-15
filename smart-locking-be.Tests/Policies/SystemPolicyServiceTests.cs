using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Policies;

public sealed class SystemPolicyServiceTests
{
    [Fact]
    [Trait("Category", "POL-01")]
    public async Task GetAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        db.AddRange(Policy(1, true), Policy(2, false));
        await db.SaveChangesAsync();
        var result = await service.GetAsync(true, 10, 10);
        Assert.Equal(1, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Equal(10, result.PageNumber);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-01")]
    public async Task GetAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var result = await service.GetAsync(true, 10, 10);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-01")]
    public async Task GetAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        db.AddRange(Policy(1, true), Policy(2, false));
        await db.SaveChangesAsync();
        var result = await service.GetAsync(false, 0, 0);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(2, Assert.Single(result.Items).Version);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var policy = Policy();
        db.Add(policy);
        await db.SaveChangesAsync();
        var result = await service.GetByIdAsync(policy.Id);
        Assert.Equal(policy.Id, result.Id);
        Assert.Equal(policy.Version, result.Version);
        // The Excel requires a Created audit on retrieval; retain this expectation to expose the mismatch.
        Audit(db, "SystemPolicy.Created", policy.Id);
    }

    [Fact]
    [Trait("Category", "POL-02")]
    public async Task GetByIdAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var policy = Policy();
        db.Add(policy);
        await db.SaveChangesAsync();
        await Error<KeyNotFoundException>(() => service.GetByIdAsync(Guid.NewGuid()), "System policy not found.");
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-02")]
    public async Task GetByIdAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var policy = Policy();
        db.Add(policy);
        await db.SaveChangesAsync();
        await Error<KeyNotFoundException>(() => service.GetByIdAsync(Guid.Empty), "System policy not found.");
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-03")]
    public async Task CreateAsync_UTC01()
    {
        await using var db = CreateDb();
        var old = Policy(4);
        db.Add(old);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var service = new SystemPolicyService(db, new Clock());
        var result = await service.CreateAsync(admin, Request());
        Assert.Equal(5, result.Version);
        Assert.False(result.IsActive);
        Assert.Equal("VND", result.Currency);
        db.ChangeTracker.Clear();
        var saved = await db.SystemPolicies.SingleAsync(p => p.Id == result.Id);
        Assert.Equal(168, saved.OverdueStartAfterHours);
        Assert.Equal(336, saved.MaxStorageHours);
        Assert.Equal(15, saved.GuestSessionTimeoutMinutes);
        Assert.Equal(admin, saved.CreatedByUserId);
        Assert.Equal(Now, saved.CreatedAt);
        Audit(db, "SystemPolicy.Created", result.Id, admin);
    }

    [Fact]
    [Trait("Category", "POL-03")]
    public async Task CreateAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        // Keep a valid request and concretize the matrix's missing-entity precondition as a missing actor.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(Guid.NewGuid(), Request()));
        Assert.Empty(db.SystemPolicies);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-03")]
    public async Task CreateAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        // Excel explicitly expects success for null; a production exception is a specification failure.
        var result = await service.CreateAsync(Guid.Empty, null!);
        Assert.NotEqual(Guid.Empty, result.Id);
        Audit(db, "SystemPolicy.Created", result.Id, Guid.Empty);
    }

    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var policy = Policy(active: false);
        db.Add(policy);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var result = await service.UpdateAsync(admin, policy.Id, Request());
        Assert.Equal(policy.Id, result.Id);
        db.ChangeTracker.Clear();
        var saved = await db.SystemPolicies.SingleAsync();
        Assert.Equal(15, saved.GuestSessionTimeoutMinutes);
        Assert.Equal("VND", saved.Currency);
        Audit(db, "SystemPolicy.Updated", policy.Id, admin);
    }

    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var policy = Policy(active: false);
        db.Add(policy);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        await Error<KeyNotFoundException>(() => service.UpdateAsync(admin, Guid.NewGuid(), Request()), "System policy not found.");
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var policy = Policy(active: true);
        db.Add(policy);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        await Error<InvalidOperationException>(() => service.UpdateAsync(admin, policy.Id, Request()), "Active policy is immutable. Create a new version instead.");
        db.ChangeTracker.Clear();
        Assert.True((await db.SystemPolicies.SingleAsync()).IsActive);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-04")]
    public async Task UpdateAsync_UTC04()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var policy = Policy(active: false);
        db.Add(policy);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        await Assert.ThrowsAsync<NullReferenceException>(() => service.UpdateAsync(Guid.Empty, Guid.Empty, null!));
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-05")]
    public async Task ActivateAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var old = Policy(1, true);
        var target = Policy(2);
        db.AddRange(old, target);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        var result = await service.ActivateAsync(admin, target.Id);
        Assert.True(result.IsActive);
        db.ChangeTracker.Clear();
        var previous = await db.SystemPolicies.SingleAsync(p => p.Id == old.Id);
        Assert.False(previous.IsActive);
        Assert.Equal(Now, previous.EffectiveTo);
        var saved = await db.SystemPolicies.SingleAsync(p => p.Id == target.Id);
        Assert.True(saved.IsActive);
        Assert.Equal(Now, saved.EffectiveFrom);
        Assert.Null(saved.EffectiveTo);
        Audit(db, "SystemPolicy.Activated", target.Id, admin);
    }

    [Fact]
    [Trait("Category", "POL-05")]
    public async Task ActivateAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var old = Policy(1, true);
        var target = Policy(2);
        db.AddRange(old, target);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        await Error<KeyNotFoundException>(() => service.ActivateAsync(admin, Guid.NewGuid()), "System policy not found.");
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "POL-05")]
    public async Task ActivateAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new SystemPolicyService(db, new Clock());
        var old = Policy(1, true);
        var target = Policy(2);
        db.AddRange(old, target);
        await db.SaveChangesAsync();
        Guid admin = Guid.NewGuid();
        await Error<KeyNotFoundException>(() => service.ActivateAsync(admin, Guid.Empty), "System policy not found.");
        Assert.Empty(db.AuditLogs);
    }

    private static SaveSystemPolicyRequest Request() => new(DeliveryApprovalMode.Manual, 15, 10, 10, 168, 5000, " vnd ", 336, 336, 24, 5, 5, 15, true, true, false, Now);

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static SystemPolicy Policy(int version = 1, bool active = false) => new() { Id = Guid.NewGuid(), Version = version, IsActive = active, Currency = "VND", CreatedByUserId = Guid.NewGuid(), EffectiveFrom = Now.AddDays(-1), OverdueStartAfterHours = 168, MaxStorageHours = 336, OverdueFeePerHour = 5000, EnableRemoteUnlock = true };
    private static async Task Error<T>(Func<Task> action, string message) where T : Exception
    {
        T exception = await Assert.ThrowsAsync<T>(action);
        if (exception is ArgumentException argument && argument.ParamName is not null)
            Assert.StartsWith(message, exception.Message);
        else
            Assert.Equal(message, exception.Message);
    }
    private static void Audit(ApplicationDbContext db, string action, Guid id, Guid? actor = null)
    {
        db.ChangeTracker.Clear();
        AuditLog log = Assert.Single(db.AuditLogs);
        Assert.Equal(action, log.Action);
        Assert.Equal(id, log.EntityId);
        Assert.Equal(actor, log.ActorUserId);
        Assert.Equal(AuditLogResult.Succeeded, log.Result);
    }
}
