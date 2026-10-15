using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Incidents;

public sealed class GuestIncidentRegressionTests
{
    [Fact]
    [Trait("Category", "INC-01")]
    public async Task Guest_report_routes_to_admin_and_rejects_cross_locker_compartment()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var locker = new Locker { Id = Guid.NewGuid(), Code = "L1", Address = "A", RecoveryAddress = "B", DeviceIdentifier = "TEST" };
        var admin = new User { Id = Guid.NewGuid(), Role = UserRole.Administrator, Status = UserStatus.Active, PasswordHash = "test" };
        db.AddRange(locker, admin);
        await db.SaveChangesAsync();
        var service = new IncidentService(db, TimeProvider.System);
        var request = new CreateGuestIncidentRequest("L1", "Other", "Door issue", "Door sensor is stuck");
        var result = await service.CreateGuestIncidentAsync(request);
        Assert.Equal(IncidentStatus.Open, result.Status);
        Assert.Equal(IncidentSource.Guest, (await db.Incidents.SingleAsync()).Source);
        Assert.Equal(admin.Id, (await db.Notifications.SingleAsync()).UserId);
        Assert.Single(db.AuditLogs);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateGuestIncidentAsync(request with { LockerCompartmentId = Guid.NewGuid() }));
        Assert.Single(db.Incidents);
    }
}
