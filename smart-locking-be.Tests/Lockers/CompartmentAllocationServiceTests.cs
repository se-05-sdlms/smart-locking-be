using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Lockers;

public sealed class CompartmentAllocationServiceTests
{
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

    #region ALLOC-01: CompartmentAllocationService.ReserveAvailableAsync
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
    [Trait("Category", "ALLOC-01")]
    public async Task ReserveAvailableAsync_UTC03()
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
        var res = await service.ReserveAvailableAsync(lockerId, Guid.Empty, null, now.AddMinutes(5));

        Assert.NotNull(res);
    }
    #endregion

    #region ALLOC-02: CompartmentAllocationService.ReleaseAsync
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

    [Fact]
    [Trait("Category", "ALLOC-02")]
    public async Task ReleaseAsync_UTC03()
    {
        await using ApplicationDbContext db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new CompartmentAllocationService(db, TimeProvider.System);

        // Guid.Empty delivery request id
        await service.ReleaseAsync(Guid.Empty, null, DateTimeOffset.UtcNow);
    }
    #endregion
}
