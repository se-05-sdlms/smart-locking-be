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
    public async Task Rules_reject_duplicates_and_updates_after_policy_activation()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var policy = new SystemPolicy { Id = Guid.NewGuid(), Currency = "VND" };
        db.SystemPolicies.Add(policy);
        await db.SaveChangesAsync();
        var service = new NotificationRuleService(db);
        Guid actor = Guid.NewGuid();
        var request = new CreateNotificationRuleRequest(policy.Id, "ParcelStored", NotificationChannel.Push, 0, true);
        var created = await service.CreateAsync(actor, request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(actor, request));
        policy.IsActive = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(actor, created.Id, new UpdateNotificationRuleRequest(5, false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new NotificationService(db, TimeProvider.System).CreateRuleAsync(request with { EventType = "OtherEvent" }, default));
        Assert.True((await service.GetByIdAsync(created.Id)).IsEnabled);
        Assert.Single(db.AuditLogs);
    }
}
