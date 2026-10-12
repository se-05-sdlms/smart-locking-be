using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class EmergencyUnlockServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public async Task Rejects_missing_or_short_reason_before_loading_hardware(string reason)
    {
        using var db = CreateDb();
        var service = new EmergencyUnlockService(db, new AccessStub(LockerAccessResult.Succeeded), TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Guid.NewGuid(), "Administrator", new EmergencyUnlockRequest(Guid.NewGuid(), Guid.NewGuid(), null, reason)));
    }

    [Theory]
    [InlineData(LockerAccessResult.Succeeded, EmergencyUnlockResult.Succeeded, AuditLogResult.Succeeded)]
    [InlineData(LockerAccessResult.Failed, EmergencyUnlockResult.Failed, AuditLogResult.Failed)]
    public async Task Admin_unlock_persists_hardware_result_and_audit(LockerAccessResult result, EmergencyUnlockResult expected, AuditLogResult audit)
    {
        using var db = CreateDb();
        Guid locker = Guid.NewGuid(), compartment = Guid.NewGuid(), user = Guid.NewGuid();
        db.LockerCompartments.Add(new LockerCompartment { Id = compartment, LockerId = locker, Code = "A1", HardwareCode = "1" });
        await db.SaveChangesAsync();
        var service = new EmergencyUnlockService(db, new AccessStub(result), TimeProvider.System);
        var response = await service.CreateAsync(user, "Administrator", new EmergencyUnlockRequest(locker, compartment, null, "Door stuck during collection"));
        Assert.Equal(expected, response.Result);
        Assert.Equal(expected, (await db.EmergencyUnlocks.SingleAsync()).Result);
        Assert.Equal(audit, (await db.AuditLogs.SingleAsync()).Result);
        Assert.Equal(user, (await db.AuditLogs.SingleAsync()).ActorUserId);
    }

    [Fact]
    public async Task Rejects_unassigned_operator_before_dispatch()
    {
        using var db = CreateDb();
        var service = new EmergencyUnlockService(db, new AccessStub(LockerAccessResult.Succeeded), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(Guid.NewGuid(), "LockerOperator", new EmergencyUnlockRequest(Guid.NewGuid(), Guid.NewGuid(), null, "Door stuck during collection")));
        Assert.Empty(db.EmergencyUnlocks);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class AccessStub(LockerAccessResult result) : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId, "test", 1, result, null, DateTimeOffset.UtcNow));
    }
}
