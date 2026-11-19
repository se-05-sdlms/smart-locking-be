using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class AuditLogServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "AUDIT")]
    public async Task GetAsync_FiltersAndPaginatesNewestFirst()
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
    public async Task GetByIdAsync_ReturnsNotFoundForUnknownRecord()
    {
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<NotFoundException>(() => new AuditLogService(db).GetByIdAsync(Guid.NewGuid()));
    }

    private static ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

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
