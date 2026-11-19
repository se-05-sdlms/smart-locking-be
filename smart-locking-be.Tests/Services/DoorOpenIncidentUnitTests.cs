using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class DoorOpenIncidentUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateStatusAsync_WhenResolvingDoorOpenTooLong_AutoResolvesSiblingDoorOpenIncidents()
    {
        await using var db = CreateDbContext();

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0900000001",
            PasswordHash = "hash",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Users.Add(adminUser);

        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "L-01",
            Address = "123 Main St",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Lockers.Add(locker);

        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            HardwareChannel = 3,
            Code = "C-03",
            DoorStatus = DoorStatus.Open,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.LockerCompartments.Add(compartment);

        var incidentPrimary = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Type = "DoorOpenTooLong",
            Source = IncidentSource.System,
            Status = IncidentStatus.Investigating,
            Title = "Cửa ngăn 3 chưa đóng",
            Description = "Cửa mở quá 10 phút",
            CreatedAt = Now.AddMinutes(-30),
            UpdatedAt = Now.AddMinutes(-30)
        };

        var incidentSibling1 = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Type = "DoorOpenTooLong",
            Source = IncidentSource.System,
            Status = IncidentStatus.Open,
            Title = "Cửa ngăn 3 chưa đóng",
            Description = "Cửa mở quá 10 phút",
            CreatedAt = Now.AddMinutes(-20),
            UpdatedAt = Now.AddMinutes(-20)
        };

        var incidentSibling2 = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Type = "DoorOpenTooLong",
            Source = IncidentSource.System,
            Status = IncidentStatus.Investigating,
            Title = "Cửa ngăn 3 chưa đóng",
            Description = "Cửa mở quá 10 phút",
            CreatedAt = Now.AddMinutes(-10),
            UpdatedAt = Now.AddMinutes(-10)
        };

        db.Incidents.AddRange(incidentPrimary, incidentSibling1, incidentSibling2);
        await db.SaveChangesAsync();

        var service = new IncidentService(
            db,
            new FixedTimeProvider(Now),
            new Sha256TokenHashService());

        var request = new UpdateIncidentStatusRequest(
            IncidentStatus.Resolved,
            null,
            "Cửa đã được nhân viên kiểm tra và đóng lại an toàn.");

        await service.UpdateStatusAsync(
            adminUser.Id,
            nameof(UserRole.Administrator),
            incidentPrimary.Id,
            request);

        var updatedPrimary = await db.Incidents.SingleAsync(i => i.Id == incidentPrimary.Id);
        var updatedSibling1 = await db.Incidents.SingleAsync(i => i.Id == incidentSibling1.Id);
        var updatedSibling2 = await db.Incidents.SingleAsync(i => i.Id == incidentSibling2.Id);

        Assert.Equal(IncidentStatus.Resolved, updatedPrimary.Status);
        Assert.Equal(IncidentStatus.Resolved, updatedSibling1.Status);
        Assert.Equal(IncidentStatus.Resolved, updatedSibling2.Status);

        Assert.Equal("Cửa đã được nhân viên kiểm tra và đóng lại an toàn.", updatedPrimary.ResolutionSummary);
        Assert.Equal("Cửa đã được nhân viên kiểm tra và đóng lại an toàn.", updatedSibling1.ResolutionSummary);
        Assert.Equal("Cửa đã được nhân viên kiểm tra và đóng lại an toàn.", updatedSibling2.ResolutionSummary);

        var actions = await db.IncidentActions.ToListAsync();
        Assert.Contains(actions, a => a.IncidentId == incidentPrimary.Id && a.ToStatus == IncidentStatus.Resolved);
        Assert.Contains(actions, a => a.IncidentId == incidentSibling1.Id && a.ToStatus == IncidentStatus.Resolved);
        Assert.Contains(actions, a => a.IncidentId == incidentSibling2.Id && a.ToStatus == IncidentStatus.Resolved);
    }

    [Fact]
    public async Task DoorOpenAlertDeduplication_WhenActiveIncidentExists_SuppressesNewAlert()
    {
        await using var db = CreateDbContext();

        var compartmentId = Guid.NewGuid();
        var alertSince = Now.AddMinutes(-10);

        db.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(),
            LockerCompartmentId = compartmentId,
            Type = "DoorOpenTooLong",
            Source = IncidentSource.System,
            Status = IncidentStatus.Open,
            Title = "Cửa ngăn 3 chưa đóng",
            Description = "Cửa mở quá lâu",
            CreatedAt = Now.AddHours(-1),
            UpdatedAt = Now.AddHours(-1)
        });
        await db.SaveChangesAsync();

        // Worker query logic: (item.Status != IncidentStatus.Resolved || item.CreatedAt >= alertSince)
        bool hasActiveAlert = await db.Incidents.AnyAsync(item =>
            item.LockerCompartmentId == compartmentId &&
            item.Type == "DoorOpenTooLong" &&
            (item.Status != IncidentStatus.Resolved || item.CreatedAt >= alertSince));

        Assert.True(hasActiveAlert);
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
