using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class MaintenanceServiceTests
{
    [Fact]
    public async Task Maintenance_tracks_creation_and_closure_and_scopes_operator_access()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var locker = new Locker { Id = Guid.NewGuid(), Code = "L1", Address = "A", RecoveryAddress = "B", DeviceIdentifier = "TEST" };
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();
        var service = new MaintenanceService(db, TimeProvider.System);
        var created = await service.CreateAsync(Guid.NewGuid(), "Administrator", new CreateMaintenanceRequest(locker.Id, null, null, MaintenancePriority.Normal, "Inspect door sensor"));
        Assert.Equal(MaintenanceStatus.Open, created.Status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(Guid.NewGuid(), "LockerOperator", created.Id, new UpdateMaintenanceRequest(MaintenanceStatus.Closed, null, "Repaired")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Guid.NewGuid(), "Administrator", created.Id, new UpdateMaintenanceRequest(MaintenanceStatus.Closed, null, " ")));
        var closed = await service.UpdateAsync(Guid.NewGuid(), "Administrator", created.Id, new UpdateMaintenanceRequest(MaintenanceStatus.Closed, null, "Repaired sensor"));
        Assert.Equal(MaintenanceStatus.Closed, closed.Status);
        var detail = await service.GetByIdAsync(Guid.NewGuid(), "Administrator", created.Id);
        Assert.Equal(2, detail.Activities.Count);
        Assert.Equal(2, await db.AuditLogs.CountAsync());
    }
}
