using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class AuditLogServiceTests
{
    [Fact]
    public async Task Search_filters_action_and_time_before_paging_without_mutating_logs()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Action = "Locker.Open", Result = AuditLogResult.Succeeded, OccurredAt = now },
            new AuditLog { Id = Guid.NewGuid(), Action = "Locker.Open", Result = AuditLogResult.Failed, OccurredAt = now.AddMinutes(-1) },
            new AuditLog { Id = Guid.NewGuid(), Action = "User.Created", Result = AuditLogResult.Succeeded, OccurredAt = now });
        await db.SaveChangesAsync();
        var service = new AuditLogService(db);
        var page = await service.GetAsync(null, null, "Locker.Open", null, null, now.AddHours(-1), now.AddHours(1), pageSize: 1);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(AuditLogResult.Succeeded, page.Items.Single().Result);
        Assert.Equal(3, await db.AuditLogs.CountAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetByIdAsync(Guid.NewGuid()));
    }
}
