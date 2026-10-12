using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class CompartmentAllocationServiceTests
{
    [Fact]
    public async Task ReserveAvailableAsync_SharesAvailabilityAcrossDeliveryAndReturn()
    {
        await using ApplicationDbContext db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        Guid lockerId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Locker locker = new()
        {
            Id = lockerId,
            Code = "L-01",
            Address = "Address",
            RecoveryAddress = "Recovery",
            DeviceIdentifier = "device-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Lockers.Add(locker);
        db.LockerCompartments.AddRange(
            CreateCompartment(locker, "A01", 1, now),
            CreateCompartment(locker, "A02", 2, now));
        await db.SaveChangesAsync();
        var service = new CompartmentAllocationService(db, TimeProvider.System);

        CompartmentReservation? deliveryReservation = await service.ReserveAvailableAsync(
            lockerId, Guid.NewGuid(), null, now.AddMinutes(10));
        CompartmentReservation? returnReservation = await service.ReserveAvailableAsync(
            lockerId, null, Guid.NewGuid(), now.AddMinutes(10));

        Assert.NotNull(deliveryReservation);
        Assert.NotNull(returnReservation);
        Assert.NotEqual(deliveryReservation.LockerCompartmentId, returnReservation.LockerCompartmentId);
    }

    private static LockerCompartment CreateCompartment(Locker locker, string code, int channel, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        LockerId = locker.Id,
        Locker = locker,
        Code = code,
        HardwareCode = code,
        HardwareChannel = channel,
        OperationalStatus = LockerCompartmentOperationalStatus.Operational,
        DoorStatus = DoorStatus.Closed,
        CreatedAt = now,
        UpdatedAt = now
    };

    #region Standardized Matrix Tests
    [Fact]
    [Trait("Category", "ALLOC-01")]
    public async Task ReserveAvailableAsync_UTC01()
    {
        await using ApplicationDbContext db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var lockerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var locker = new Locker { Id = lockerId, Code = "L-01", Address = "A", RecoveryAddress = "R", DeviceIdentifier = "D", OperationalStatus = LockerOperationalStatus.Operational };
        db.Lockers.Add(locker);
        db.LockerCompartments.Add(CreateCompartment(locker, "A01", 1, now));
        await db.SaveChangesAsync();

        var service = new CompartmentAllocationService(db, TimeProvider.System);
        var res = await service.ReserveAvailableAsync(lockerId, Guid.NewGuid(), null, now.AddMinutes(10));

        Assert.NotNull(res);
    }

    [Fact]
    [Trait("Category", "ALLOC-01")]
    public async Task ReserveAvailableAsync_UTC02()
    {
        await using ApplicationDbContext db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new CompartmentAllocationService(db, TimeProvider.System);

        // Locker has no available compartments
        var res = await service.ReserveAvailableAsync(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Null(res);
    }

    [Fact]
    [Trait("Category", "ALLOC-02")]
    public async Task ReleaseAsync_UTC01()
    {
        await using ApplicationDbContext db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var deliveryId = Guid.NewGuid();
        var res = new CompartmentReservation
        {
            Id = Guid.NewGuid(),
            LockerCompartmentId = Guid.NewGuid(),
            DeliveryRequestId = deliveryId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.CompartmentReservations.Add(res);
        await db.SaveChangesAsync();

        var service = new CompartmentAllocationService(db, TimeProvider.System);
        await service.ReleaseAsync(deliveryId, null, DateTimeOffset.UtcNow);

        var updated = await db.CompartmentReservations.FindAsync(res.Id);
        Assert.NotNull(updated!.ReleasedAt);
    }

    [Fact]
    [Trait("Category", "ALLOC-02")]
    public async Task ReleaseAsync_UTC02()
    {
        await using ApplicationDbContext db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new CompartmentAllocationService(db, TimeProvider.System);

        // No matching reservation, should not throw
        await service.ReleaseAsync(Guid.NewGuid(), null, DateTimeOffset.UtcNow);
    }
    #endregion
}
