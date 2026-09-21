using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operators;

public sealed class OperatorServiceTests
{
    [Fact]
    public async Task Dashboard_UsesOnlyActiveAssignmentsAndCurrentParcelDeadlines()
    {
        await using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        var other = Guid.NewGuid();
        var compartmentId = Guid.NewGuid();
        db.Lockers.AddRange(Locker(assigned, "LK-01"), Locker(revoked, "LK-02"), Locker(other, "LK-03"));
        db.OperatorAssignments.AddRange(
            new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = assigned },
            new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = revoked, RevokedAt = now },
            new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = Guid.NewGuid(), LockerId = other });
        db.LockerCompartments.Add(new LockerCompartment { Id = compartmentId, LockerId = assigned, Code = "N01", OperationalStatus = LockerCompartmentOperationalStatus.Operational });
        var deliveryId = Guid.NewGuid();
        db.DeliveryRequests.Add(new DeliveryRequest { Id = deliveryId, LockerId = assigned, AllocatedCompartmentId = compartmentId });
        db.Parcels.Add(new Parcel { Id = Guid.NewGuid(), DeliveryRequestId = deliveryId, ParcelCode = "P1", Status = ParcelStatus.Stored, PickupDueAt = now.AddHours(-2), StoredAt = now.AddDays(-2), MaxStorageUntil = now.AddDays(2) });
        db.Incidents.AddRange(
            new Incident { Id = Guid.NewGuid(), LockerId = assigned, Status = IncidentStatus.Open },
            new Incident { Id = Guid.NewGuid(), LockerId = revoked, Status = IncidentStatus.Open },
            new Incident { Id = Guid.NewGuid(), LockerId = other, Status = IncidentStatus.Open });
        await db.SaveChangesAsync();

        var result = await new OperatorService(db).GetDashboardAsync(operatorId);

        Assert.Equal(1, result.Lockers.Total);
        Assert.Equal(1, result.Parcels.Overdue);
        Assert.Equal(1, result.Incidents.Open);
        Assert.Equal(1, result.Compartments.Overdue);
        Assert.Equal("P1", Assert.Single(result.RecentOverdueParcels).ParcelCode);
    }

    [Fact]
    public async Task Lists_DoNotExposeUnassignedRecordsEvenWhenFilteredById()
    {
        await using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.NewGuid();
        var owned = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        db.Lockers.AddRange(Locker(owned, "LK-01"), Locker(foreign, "LK-02"));
        db.OperatorAssignments.Add(new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = owned });
        db.Incidents.AddRange(
            new Incident { Id = Guid.NewGuid(), LockerId = owned, Status = IncidentStatus.Open },
            new Incident { Id = Guid.NewGuid(), LockerId = foreign, Status = IncidentStatus.Open });
        var deliveryId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        db.DeliveryRequests.Add(new DeliveryRequest { Id = deliveryId, LockerId = foreign });
        db.Parcels.Add(new Parcel { Id = parcelId, DeliveryRequestId = deliveryId, Status = ParcelStatus.Overdue, StoredAt = now.AddDays(-2), PickupDueAt = now.AddDays(-1), MaxStorageUntil = now.AddDays(2) });
        await db.SaveChangesAsync();

        var service = new OperatorService(db);
        Assert.Equal(0, (await service.GetIncidentsAsync(operatorId, foreign)).TotalCount);
        Assert.Equal(1, (await service.GetIncidentsAsync(operatorId)).TotalCount);
        Assert.Equal(0, (await service.GetOverdueParcelsAsync(operatorId, foreign)).TotalCount);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOverdueParcelAsync(operatorId, parcelId));
    }

    [Fact]
    public async Task Dashboard_PartitionsCompartmentsIncludingReturnsReservationsAndUnavailable()
    {
        await using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.NewGuid();
        var lockerId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        db.Lockers.Add(Locker(lockerId, "LK-01"));
        db.OperatorAssignments.Add(new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = lockerId });
        for (int index = 0; index < ids.Length; index++)
            db.LockerCompartments.Add(new LockerCompartment
            {
                Id = ids[index],
                LockerId = lockerId,
                Code = $"N{index}",
                OperationalStatus = index == 4 ? LockerCompartmentOperationalStatus.OutOfService : LockerCompartmentOperationalStatus.Operational
            });
        for (int index = 0; index < 2; index++)
        {
            var deliveryId = Guid.NewGuid();
            db.DeliveryRequests.Add(new DeliveryRequest { Id = deliveryId, LockerId = lockerId, AllocatedCompartmentId = ids[index] });
            db.Parcels.Add(new Parcel
            {
                Id = Guid.NewGuid(),
                DeliveryRequestId = deliveryId,
                Status = ParcelStatus.Stored,
                StoredAt = now.AddDays(-2),
                PickupDueAt = index == 0 ? now.AddDays(1) : now.AddHours(-1),
                MaxStorageUntil = now.AddDays(2)
            });
        }
        db.ReturnRequests.Add(new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            AllocatedCompartmentId = ids[2],
            Status = ReturnRequestStatus.Deposited
        });
        db.CompartmentReservations.Add(new CompartmentReservation
        {
            Id = Guid.NewGuid(),
            LockerCompartmentId = ids[3],
            ExpiresAt = now.AddHours(1),
            ReservedAt = now.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var result = await new OperatorService(db).GetDashboardAsync(operatorId);

        Assert.Equal(6, result.Compartments.Total);
        Assert.Equal(1, result.Compartments.Available);
        Assert.Equal(2, result.Compartments.Occupied);
        Assert.Equal(1, result.Compartments.Overdue);
        Assert.Equal(1, result.Compartments.Reserved);
        Assert.Equal(1, result.Compartments.Unavailable);
    }

    [Fact]
    public async Task OverdueList_FiltersByDurationAndReturnsMaskedResidentDetails()
    {
        await using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.NewGuid();
        var lockerId = Guid.NewGuid();
        var residentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        db.Lockers.Add(Locker(lockerId, "LK-01"));
        db.OperatorAssignments.Add(new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = lockerId });
        db.Users.Add(new User { Id = userId, PhoneNumber = "0901234567" });
        db.ResidentProfiles.Add(new ResidentProfile { Id = residentId, UserId = userId, FullName = "Test Resident" });
        db.DeliveryRequests.Add(new DeliveryRequest { Id = deliveryId, LockerId = lockerId, ResidentProfileId = residentId });
        db.Parcels.Add(new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryId,
            ParcelCode = "PKG-123",
            Status = ParcelStatus.Overdue,
            StoredAt = now.AddDays(-5),
            PickupDueAt = now.AddDays(-4),
            MaxStorageUntil = now.AddDays(1)
        });
        await db.SaveChangesAsync();

        var service = new OperatorService(db);
        var old = await service.GetOverdueParcelsAsync(operatorId, search: "pkg", threeDaysOrMore: true);
        var recent = await service.GetOverdueParcelsAsync(operatorId, threeDaysOrMore: false);

        Assert.Equal(1, old.TotalCount);
        Assert.Equal("090 *** 4567", Assert.Single(old.Items).ResidentPhoneMasked);
        Assert.Equal("Test Resident", old.Items[0].ResidentName);
        Assert.Equal(0, recent.TotalCount);
    }

    [Fact]
    public async Task Incidents_LinkedToAParcelWithoutLockerIdStillUseTheParcelLockerScope()
    {
        await using var db = NewContext();
        var operatorId = Guid.NewGuid();
        var lockerId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        db.Lockers.Add(Locker(lockerId, "LK-01"));
        db.OperatorAssignments.Add(new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = operatorId, LockerId = lockerId });
        db.DeliveryRequests.Add(new DeliveryRequest { Id = deliveryId, LockerId = lockerId });
        db.Parcels.Add(new Parcel { Id = parcelId, DeliveryRequestId = deliveryId });
        db.Incidents.Add(new Incident { Id = Guid.NewGuid(), ParcelId = parcelId, Status = IncidentStatus.Open });
        await db.SaveChangesAsync();

        var service = new OperatorService(db);
        var list = await service.GetIncidentsAsync(operatorId);
        var dashboard = await service.GetDashboardAsync(operatorId);

        Assert.Equal(lockerId, Assert.Single(list.Items).LockerId);
        Assert.Equal(1, dashboard.Incidents.Open);
    }

    private static ApplicationDbContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Locker Locker(Guid id, string code) => new()
    {
        Id = id,
        Code = code,
        Address = code,
        OperationalStatus = LockerOperationalStatus.Operational,
        ConnectionStatus = LockerConnectionStatus.Online
    };
}
