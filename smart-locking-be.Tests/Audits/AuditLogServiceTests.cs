using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Audits;

public sealed class AuditLogServiceTests
{
    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new AuditLogService(db);
        Guid actor = Guid.NewGuid();
        Guid entity = Guid.NewGuid();
        var matching = Log(actor, entity, Now);
        db.AddRange(matching, Log(Guid.NewGuid(), entity, Now), Log(actor, entity, Now.AddDays(-2)), Log(actor, entity, Now.AddDays(1)));
        await db.SaveChangesAsync();
        var result = await service.GetAsync(" created ", actor, " Created ", " User ", entity, Now.AddDays(-1), Now.AddDays(1));
        Assert.Equal(1, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(matching.Id, item.Id);
        Assert.Equal(actor, item.ActorUserId);
        Assert.Equal(entity, item.EntityId);
        Assert.Equal("created user", item.Details);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new AuditLogService(db);
        Guid actor = Guid.NewGuid();
        Guid entity = Guid.NewGuid();
        var result = await service.GetAsync("missing", actor, "Created", "User", entity, Now.AddDays(-1), Now.AddDays(1));
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new AuditLogService(db);
        Guid actor = Guid.NewGuid();
        Guid entity = Guid.NewGuid();
        var matching = Log(Guid.Empty, Guid.Empty, Now);
        db.AddRange(matching, Log(actor, entity, Now));
        await db.SaveChangesAsync();
        var result = await service.GetAsync("", Guid.Empty, "", "", Guid.Empty, null, null, pageNumber: 0, pageSize: 0);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(matching.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    [Trait("Category", "AUD-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new AuditLogService(db);
        var log = Log(null, Guid.NewGuid(), Now);
        db.Add(log);
        await db.SaveChangesAsync();
        var result = await service.GetByIdAsync(log.Id);
        Assert.Equal(log.Id, result.Id);
        Assert.Equal("System/Guest", result.Actor);
        Assert.Equal("Created", result.Action);
        Assert.Equal(log.EntityId, result.EntityId);
        Assert.Equal(Now, result.OccurredAt);
        Assert.Equal(log.Result, result.Result);
    }

    [Fact]
    [Trait("Category", "AUD-02")]
    public async Task GetByIdAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new AuditLogService(db);
        var log = Log(null, Guid.NewGuid(), Now);
        db.Add(log);
        await db.SaveChangesAsync();
        await Error<KeyNotFoundException>(() => service.GetByIdAsync(Guid.NewGuid()), "Audit log not found.");
        Assert.Single(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "AUD-02")]
    public async Task GetByIdAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new AuditLogService(db);
        var log = Log(null, Guid.NewGuid(), Now);
        db.Add(log);
        await db.SaveChangesAsync();
        await Error<KeyNotFoundException>(() => service.GetByIdAsync(Guid.Empty), "Audit log not found.");
        Assert.Single(db.AuditLogs);
    }

    private static AuditLog Log(Guid? actor, Guid entity, DateTimeOffset at) => new() { Id = Guid.NewGuid(), ActorUserId = actor, EntityId = entity, EntityType = "User", Action = "Created", Details = "created user", Result = AuditLogResult.Succeeded, OccurredAt = at };

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task Error<T>(Func<Task> action, string message) where T : Exception
    {
        T exception = await Assert.ThrowsAsync<T>(action);
        if (exception is ArgumentException argument && argument.ParamName is not null)
            Assert.StartsWith(message, exception.Message);
        else
            Assert.Equal(message, exception.Message);
    }
}
