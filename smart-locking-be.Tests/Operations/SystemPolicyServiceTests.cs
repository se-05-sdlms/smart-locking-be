using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class SystemPolicyServiceTests
{
    [Fact]
    public async Task Activation_replaces_previous_policy_and_active_versions_cannot_be_edited()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new SystemPolicyService(db, TimeProvider.System);
        Guid actor = Guid.NewGuid();
        var request = new SaveSystemPolicyRequest(DeliveryApprovalMode.Manual, 10, 10, 10, 168, 0, "VND", 336, 336, 24, 5, 5, 10, true, true, false, DateTimeOffset.UtcNow);
        var first = await service.CreateAsync(actor, request);
        await service.ActivateAsync(actor, first.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(actor, first.Id, request));
        var second = await service.CreateAsync(actor, request);
        await service.ActivateAsync(actor, second.Id);
        Assert.Equal(first.Version + 1, second.Version);
        Assert.Equal(second.Id, (await db.SystemPolicies.SingleAsync(item => item.IsActive)).Id);
        Assert.NotNull((await db.SystemPolicies.SingleAsync(item => item.Id == first.Id)).EffectiveTo);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(actor, request with { GuestSessionTimeoutMinutes = 0 }));
        Assert.Equal(4, await db.AuditLogs.CountAsync());
    }
}
