using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Audits;

public sealed class AuditLogServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    #region AUD-01: GetAsync
    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Email = "admin@boxora.com", PhoneNumber = "0912345678", Role = UserRole.Administrator, Status = UserStatus.Active };
        var log1 = new AuditLog { Id = Guid.NewGuid(), ActorUserId = user.Id, Action = "A1", EntityType = "User", Result = AuditLogResult.Succeeded, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var log2 = new AuditLog { Id = Guid.NewGuid(), ActorUserId = user.Id, Action = "A2", EntityType = "User", Result = AuditLogResult.Succeeded, OccurredAt = DateTimeOffset.UtcNow };
        dbContext.AddRange(user, log1, log2);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        PagedResult<AuditLogResponse> result = await service.GetAsync(null, null, null, null, null, null, null, default, 1, 20);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("A2", result.Items[0].Action);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var log = new AuditLog { Id = Guid.NewGuid(), Action = "DeliveryRequest.Created", EntityType = "DeliveryRequest", Result = AuditLogResult.Succeeded, OccurredAt = DateTimeOffset.UtcNow };
        dbContext.Add(log);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        PagedResult<AuditLogResponse> result = await service.GetAsync("Created", null, null, null, null, null, null, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal("DeliveryRequest.Created", result.Items[0].Action);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var log = new AuditLog { Id = Guid.NewGuid(), Action = "Action", EntityType = "Locker", Details = "LockerId=11111111-1111-1111-1111-111111111111", Result = AuditLogResult.Succeeded, OccurredAt = DateTimeOffset.UtcNow };
        dbContext.Add(log);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        PagedResult<AuditLogResponse> result = await service.GetAsync("LockerId", null, null, null, null, null, null, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Contains("LockerId", result.Items[0].Details);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var log = new AuditLog { Id = Guid.NewGuid(), Action = "Action", EntityType = "Locker", Details = null, Result = AuditLogResult.Succeeded, OccurredAt = DateTimeOffset.UtcNow };
        dbContext.Add(log);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        PagedResult<AuditLogResponse> result = await service.GetAsync("missing", null, null, null, null, null, null, default, 1, 10);

        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var actorId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var log1 = new AuditLog { Id = Guid.NewGuid(), ActorUserId = actorId, Action = "MatchAction", EntityType = "EntityX", EntityId = entityId, Result = AuditLogResult.Succeeded, OccurredAt = DateTimeOffset.UtcNow };
        var log2 = new AuditLog { Id = Guid.NewGuid(), ActorUserId = Guid.NewGuid(), Action = "OtherAction", EntityType = "EntityY", EntityId = Guid.NewGuid(), Result = AuditLogResult.Succeeded, OccurredAt = DateTimeOffset.UtcNow };
        dbContext.AddRange(log1, log2);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        PagedResult<AuditLogResponse> result = await service.GetAsync(null, actorId, "MatchAction", "EntityX", entityId, null, null, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal(log1.Id, result.Items[0].Id);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new AuditLog { Id = Guid.NewGuid(), Action = "Before", EntityType = "X", Result = AuditLogResult.Succeeded, OccurredAt = from.AddDays(-1) };
        var inside = new AuditLog { Id = Guid.NewGuid(), Action = "Inside", EntityType = "X", Result = AuditLogResult.Succeeded, OccurredAt = from.AddDays(5) };
        var atTo = new AuditLog { Id = Guid.NewGuid(), Action = "AtTo", EntityType = "X", Result = AuditLogResult.Succeeded, OccurredAt = to };
        dbContext.AddRange(before, inside, atTo);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        PagedResult<AuditLogResponse> result = await service.GetAsync(null, null, null, null, null, from, to, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal("Inside", result.Items[0].Action);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext);

        PagedResult<AuditLogResponse> result = await service.GetAsync(null, null, null, null, null, null, null, default, 0, 0);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext);

        PagedResult<AuditLogResponse> result = await service.GetAsync(null, null, null, null, null, null, null, default, 1, 101);

        Assert.Equal(100, result.PageSize);
    }
    #endregion

    #region AUD-02: GetByIdAsync
    [Fact]
    [Trait("Category", "AUD-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = "Detail.Action",
            EntityType = "Locker",
            EntityId = Guid.NewGuid(),
            Result = AuditLogResult.Succeeded,
            OccurredAt = DateTimeOffset.UtcNow
        };
        dbContext.Add(log);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        AuditLogResponse result = await service.GetByIdAsync(log.Id, default);

        Assert.Equal(log.Id, result.Id);
        Assert.Equal("Detail.Action", result.Action);
    }

    [Fact]
    [Trait("Category", "AUD-02")]
    public async Task GetByIdAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid(), default));
    }

    [Fact]
    [Trait("Category", "AUD-02")]
    public async Task GetByIdAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.Empty, default));
    }
    #endregion
}
