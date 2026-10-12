using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

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
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            Action = "Test.Action",
            EntityType = "User",
            EntityId = user.Id,
            Result = AuditLogResult.Succeeded,
            OccurredAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(user, log);
        await dbContext.SaveChangesAsync();

        var service = new AuditLogService(dbContext);
        PagedResult<AuditLogResponse> result = await service.GetAsync(null, null, null, null, null, null, null, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal("Test.Action", result.Items[0].Action);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext);

        PagedResult<AuditLogResponse> result = await service.GetAsync(null, null, "NonExistentAction", null, null, null, null, default, 1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "AUD-01")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext);

        // Boundary: pageNumber = 0, pageSize = 0 (clamped to min)
        PagedResult<AuditLogResponse> result = await service.GetAsync(null, null, null, null, null, null, null, default, 0, 0);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
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
