using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Lockers;

public sealed class LockerAvailabilityUnitTests
{
    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Locker CreateLocker(
        Guid? id = null,
        LockerOperationalStatus status = LockerOperationalStatus.Operational,
        string code = "LOCKER-01") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Code = code,
        DeviceIdentifier = $"DEV-{code}",
        Address = "123 Test St",
        RecoveryAddress = "Recovery St",
        OperationalStatus = status,
        ConnectionStatus = LockerConnectionStatus.Online,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static LockerCompartment CreateCompartment(
        Locker locker,
        Guid? id = null,
        LockerCompartmentOperationalStatus status = LockerCompartmentOperationalStatus.Operational,
        string code = "C01",
        int channel = 1) => new()
    {
        Id = id ?? Guid.NewGuid(),
        LockerId = locker.Id,
        Locker = locker,
        Code = code,
        HardwareCode = $"HW-{code}",
        HardwareChannel = channel,
        OperationalStatus = status,
        DoorStatus = DoorStatus.Closed,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task OperationalLockers_WhenLockerIsOperationalAndNoMaintenanceOrIncident_ReturnsLocker()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Single(result);
        Assert.Equal(locker.Id, result[0].Id);
    }

    [Theory]
    [InlineData(LockerOperationalStatus.Inactive)]
    [InlineData(LockerOperationalStatus.OutOfService)]
    public async Task OperationalLockers_WhenLockerIsInactiveOrOutOfService_ExcludesLocker(LockerOperationalStatus status)
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker(status: status);
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(MaintenanceStatus.Open)]
    [InlineData(MaintenanceStatus.InProgress)]
    public async Task OperationalLockers_WhenLockerHasOpenOrInProgressMaintenance_ExcludesLocker(MaintenanceStatus status)
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            Status = status,
            Description = "Locker maintenance"
        };
        db.MaintenanceRequests.Add(maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(IncidentStatus.Open)]
    [InlineData(IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Escalated)]
    public async Task OperationalLockers_WhenLockerHasClosedMaintenanceWithUnresolvedIncident_ExcludesLocker(IncidentStatus incidentStatus)
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Type = "locker",
            Status = incidentStatus,
            Title = "Hardware failure"
        };
        db.Incidents.Add(incident);

        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            IncidentId = incident.Id,
            Incident = incident,
            Status = MaintenanceStatus.Closed,
            Description = "Closed but incident still active"
        };
        db.MaintenanceRequests.Add(maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task OperationalLockers_WhenLockerHasClosedMaintenanceWithResolvedIncident_ReturnsLocker()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Type = "locker",
            Status = IncidentStatus.Resolved,
            Title = "Fixed hardware"
        };
        db.Incidents.Add(incident);

        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            IncidentId = incident.Id,
            Incident = incident,
            Status = MaintenanceStatus.Closed,
            Description = "Repaired and verified"
        };
        db.MaintenanceRequests.Add(maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Single(result);
        Assert.Equal(locker.Id, result[0].Id);
    }

    [Fact]
    public async Task OperationalLockers_WhenLockerHasClosedMaintenanceWithNoIncident_ReturnsLocker()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            IncidentId = null,
            Status = MaintenanceStatus.Closed,
            Description = "Routine inspection done"
        };
        db.MaintenanceRequests.Add(maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Single(result);
        Assert.Equal(locker.Id, result[0].Id);
    }

    [Fact]
    public async Task OperationalLockers_WhenLockerHasCancelledMaintenance_ReturnsLocker()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            Status = MaintenanceStatus.Cancelled,
            Description = "False alarm"
        };
        db.MaintenanceRequests.Add(maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Single(result);
        Assert.Equal(locker.Id, result[0].Id);
    }

    [Theory]
    [InlineData("locker", IncidentStatus.Open)]
    [InlineData("LOCKER", IncidentStatus.Investigating)]
    [InlineData("compartment", IncidentStatus.Escalated)]
    public async Task OperationalLockers_WhenLockerHasUnresolvedEquipmentIncident_ExcludesLocker(string type, IncidentStatus status)
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            Type = type,
            Status = status,
            Title = "Equipment failure"
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task OperationalLockers_WhenLockerIncidentIsResolved_ReturnsLocker()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            Type = "locker",
            Status = IncidentStatus.Resolved,
            Title = "Resolved incident"
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Single(result);
        Assert.Equal(locker.Id, result[0].Id);
    }

    [Fact]
    public async Task OperationalLockers_WhenIncidentIsNotLockerOrCompartmentType_DoesNotBlockLocker()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            Type = "delivery",
            Status = IncidentStatus.Open,
            Title = "Delivery delay"
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Single(result);
        Assert.Equal(locker.Id, result[0].Id);
    }

    [Fact]
    public async Task OperationalLockers_WhenMaintenanceOrIncidentIsForCompartmentOnly_DoesNotBlockLocker()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker);
        db.AddRange(locker, compartment);

        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Status = MaintenanceStatus.Open,
            Description = "Door latch broken"
        };
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Type = "compartment",
            Status = IncidentStatus.Open,
            Title = "Compartment sensor broken"
        };
        db.AddRange(maintenance, incident);
        await db.SaveChangesAsync();

        var result = await db.OperationalLockers().ToListAsync();

        Assert.Single(result);
        Assert.Equal(locker.Id, result[0].Id);
    }

    [Fact]
    public async Task OperationalCompartments_WhenCompartmentAndParentLockerAreOperational_ReturnsCompartment()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Single(result);
        Assert.Equal(compartment.Id, result[0].Id);
    }

    [Theory]
    [InlineData(LockerOperationalStatus.Inactive)]
    [InlineData(LockerOperationalStatus.OutOfService)]
    public async Task OperationalCompartments_WhenParentLockerStatusIsNotOperational_ExcludesCompartment(LockerOperationalStatus lockerStatus)
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker(status: lockerStatus);
        var compartment = CreateCompartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task OperationalCompartments_WhenParentLockerHasBlockingMaintenance_ExcludesCompartment()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker);
        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = null,
            Status = MaintenanceStatus.Open,
            Description = "Main power unit repair"
        };
        db.AddRange(locker, compartment, maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(LockerCompartmentOperationalStatus.Inactive)]
    [InlineData(LockerCompartmentOperationalStatus.OutOfService)]
    public async Task OperationalCompartments_WhenCompartmentStatusIsNotOperational_ExcludesCompartment(LockerCompartmentOperationalStatus compStatus)
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker, status: compStatus);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(MaintenanceStatus.Open)]
    [InlineData(MaintenanceStatus.InProgress)]
    public async Task OperationalCompartments_WhenCompartmentHasOpenOrInProgressMaintenance_ExcludesCompartment(MaintenanceStatus status)
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker);
        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Status = status,
            Description = "Hinge stuck"
        };
        db.AddRange(locker, compartment, maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task OperationalCompartments_WhenCompartmentHasClosedMaintenanceWithUnresolvedIncident_ExcludesCompartment()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Type = "compartment",
            Status = IncidentStatus.Open,
            Title = "Lock broken"
        };
        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            IncidentId = incident.Id,
            Incident = incident,
            Status = MaintenanceStatus.Closed,
            Description = "Closed but incident not resolved"
        };
        db.AddRange(locker, compartment, incident, maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task OperationalCompartments_WhenCompartmentHasUnresolvedIncident_ExcludesCompartment()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Type = "compartment",
            Status = IncidentStatus.Open,
            Title = "Sensor damaged"
        };
        db.AddRange(locker, compartment, incident);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task OperationalCompartments_WhenCompartmentIncidentIsResolved_ReturnsCompartment()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment = CreateCompartment(locker);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            Type = "compartment",
            Status = IncidentStatus.Resolved,
            Title = "Fixed sensor"
        };
        db.AddRange(locker, compartment, incident);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Single(result);
        Assert.Equal(compartment.Id, result[0].Id);
    }

    [Fact]
    public async Task OperationalCompartments_WhenSiblingCompartmentHasMaintenance_DoesNotBlockOtherCompartment()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        var compartment1 = CreateCompartment(locker, code: "C01", channel: 1);
        var compartment2 = CreateCompartment(locker, code: "C02", channel: 2);
        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment1.Id,
            Status = MaintenanceStatus.Open,
            Description = "C01 is jammed"
        };
        db.AddRange(locker, compartment1, compartment2, maintenance);
        await db.SaveChangesAsync();

        var result = await db.OperationalCompartments().ToListAsync();

        Assert.Single(result);
        Assert.Equal(compartment2.Id, result[0].Id);
    }

    [Fact]
    public async Task BlockingMaintenance_FiltersExpectedStatuses()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var open = new MaintenanceRequest { Id = Guid.NewGuid(), LockerId = locker.Id, Status = MaintenanceStatus.Open, Description = "1" };
        var inProg = new MaintenanceRequest { Id = Guid.NewGuid(), LockerId = locker.Id, Status = MaintenanceStatus.InProgress, Description = "2" };
        var cancelled = new MaintenanceRequest { Id = Guid.NewGuid(), LockerId = locker.Id, Status = MaintenanceStatus.Cancelled, Description = "3" };
        var closedNoInc = new MaintenanceRequest { Id = Guid.NewGuid(), LockerId = locker.Id, Status = MaintenanceStatus.Closed, Description = "4" };

        var incUnresolved = new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, Type = "locker", Status = IncidentStatus.Open };
        var closedUnresolvedInc = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Status = MaintenanceStatus.Closed,
            IncidentId = incUnresolved.Id,
            Incident = incUnresolved,
            Description = "5"
        };

        var incResolved = new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, Type = "locker", Status = IncidentStatus.Resolved };
        var closedResolvedInc = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Status = MaintenanceStatus.Closed,
            IncidentId = incResolved.Id,
            Incident = incResolved,
            Description = "6"
        };

        db.Incidents.AddRange(incUnresolved, incResolved);
        db.MaintenanceRequests.AddRange(open, inProg, cancelled, closedNoInc, closedUnresolvedInc, closedResolvedInc);
        await db.SaveChangesAsync();

        var blocking = await db.BlockingMaintenance().Select(m => m.Id).ToListAsync();

        Assert.Contains(open.Id, blocking);
        Assert.Contains(inProg.Id, blocking);
        Assert.Contains(closedUnresolvedInc.Id, blocking);

        Assert.DoesNotContain(cancelled.Id, blocking);
        Assert.DoesNotContain(closedNoInc.Id, blocking);
        Assert.DoesNotContain(closedResolvedInc.Id, blocking);
    }

    [Fact]
    public async Task BlockingEquipmentIncidents_FiltersExpectedTypesAndStatuses()
    {
        await using var db = CreateDbContext();
        var locker = CreateLocker();
        db.Lockers.Add(locker);

        var openLocker = new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, Type = "locker", Status = IncidentStatus.Open };
        var openComp = new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, Type = "compartment", Status = IncidentStatus.Open };
        var resolvedLocker = new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, Type = "locker", Status = IncidentStatus.Resolved };
        var otherType = new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, Type = "software", Status = IncidentStatus.Open };

        db.Incidents.AddRange(openLocker, openComp, resolvedLocker, otherType);
        await db.SaveChangesAsync();

        var blocking = await db.BlockingEquipmentIncidents().Select(i => i.Id).ToListAsync();

        Assert.Contains(openLocker.Id, blocking);
        Assert.Contains(openComp.Id, blocking);
        Assert.DoesNotContain(resolvedLocker.Id, blocking);
        Assert.DoesNotContain(otherType.Id, blocking);
    }
}
