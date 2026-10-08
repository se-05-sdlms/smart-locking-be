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
}
