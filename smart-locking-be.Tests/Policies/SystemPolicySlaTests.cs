using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Policies;

public sealed class SystemPolicySlaTests
{
    private static readonly IncidentSlaPolicy Custom = new(7, 17, 27, 37, 47, 57, 67, 77);

    [Fact]
    public async Task FeeOnlyUpdatePreservesAllCustomSlaTargetsAfterReload()
    {
        await using var db = Database();
        var service = new SystemPolicyService(db, TimeProvider.System);
        var created = await service.CreateAsync(Guid.NewGuid(), Request(Custom));
        db.ChangeTracker.Clear();
        var updated = await service.UpdateAsync(Guid.NewGuid(), created.Id, Request(null) with { OverdueFeePerHour = 123m });
        db.ChangeTracker.Clear();
        var reloaded = await service.GetByIdAsync(updated.Id);
        Assert.Equal(Custom, reloaded.IncidentSla);
        Assert.Equal(123m, reloaded.OverdueFeePerHour);
    }

    [Fact]
    public async Task CreateWithoutSlaUsesDocumentedDefaults()
    {
        await using var db = Database();
        var service = new SystemPolicyService(db, TimeProvider.System);
        var created = await service.CreateAsync(Guid.NewGuid(), Request(null));
        db.ChangeTracker.Clear();
        Assert.Equal(new IncidentSlaPolicy(), (await service.GetByIdAsync(created.Id)).IncidentSla);
    }

    [Fact]
    public async Task ExplicitSlaUpdateReplacesAllTargets()
    {
        await using var db = Database();
        var service = new SystemPolicyService(db, TimeProvider.System);
        var created = await service.CreateAsync(Guid.NewGuid(), Request(null));
        await service.UpdateAsync(Guid.NewGuid(), created.Id, Request(Custom));
        db.ChangeTracker.Clear();
        Assert.Equal(Custom, (await service.GetByIdAsync(created.Id)).IncidentSla);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(20, 19)]
    public async Task InvalidExplicitSlaDoesNotChangePersistedVersion(int acknowledge, int resolve)
    {
        await using var db = Database();
        var service = new SystemPolicyService(db, TimeProvider.System);
        var created = await service.CreateAsync(Guid.NewGuid(), Request(Custom));
        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(Guid.NewGuid(), created.Id,
            Request(Custom with { CriticalAcknowledgeMinutes = acknowledge, CriticalResolveMinutes = resolve })));
        db.ChangeTracker.Clear();
        Assert.Equal(Custom, (await service.GetByIdAsync(created.Id)).IncidentSla);
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SaveSystemPolicyRequest Request(IncidentSlaPolicy? sla) => new(
        DeliveryApprovalMode.Auto, 15, 30, 10, 168, 0m, "VND", 336, 336, 24, 5,
        5, 15, true, true, false, DateTimeOffset.UtcNow, IncidentSla: sla);
}
