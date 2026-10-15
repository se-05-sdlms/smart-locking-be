using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class CompartmentAllocationServiceTests
{
    [Fact]
    [Trait("Category", "ALLOC-01")]
    public async Task ReserveAvailableAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new CompartmentAllocationService(db, new Clock());
        var locker = Locker();
        var compartment = Compartment(locker);
        Guid delivery = Guid.NewGuid();
        var unavailable = Compartment(locker, "C00");
        unavailable.DoorStatus = DoorStatus.Open;
        db.AddRange(locker, compartment, unavailable);
        await db.SaveChangesAsync();
        var result = await service.ReserveAvailableAsync(locker.Id, delivery, null, Now.AddMinutes(10));
        Assert.NotNull(result);
        Assert.Equal(compartment.Id, result.LockerCompartmentId);
        Assert.Equal(delivery, result.DeliveryRequestId);
        Assert.Null(result.ReturnRequestId);
        db.ChangeTracker.Clear();
        var saved = Assert.Single(db.CompartmentReservations);
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal(Now, saved.ReservedAt);
        Assert.Equal(Now.AddMinutes(10), saved.ExpiresAt);
        Assert.Null(saved.ReleasedAt);
    }

    [Fact]
    [Trait("Category", "ALLOC-01")]
    public async Task ReserveAvailableAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new CompartmentAllocationService(db, new Clock());
        var locker = Locker();
        var compartment = Compartment(locker);
        Guid delivery = Guid.NewGuid();
        Assert.Null(await service.ReserveAvailableAsync(locker.Id, delivery, null, Now.AddMinutes(10)));
        Assert.Empty(db.CompartmentReservations);
    }

    [Fact]
    [Trait("Category", "ALLOC-01")]
    public async Task ReserveAvailableAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new CompartmentAllocationService(db, new Clock());
        var locker = Locker();
        var compartment = Compartment(locker);
        Guid delivery = Guid.NewGuid();
        // Excel marks this both-owner boundary as no exception. Preserve it to expose the exclusive-owner conflict.
        var result = await service.ReserveAvailableAsync(Guid.Empty, Guid.Empty, Guid.Empty, default);
        Assert.Null(result);
        Assert.Empty(db.CompartmentReservations);
    }

    [Fact]
    [Trait("Category", "ALLOC-02")]
    public async Task ReleaseAsync_UTC01()
    {
        await using var db = CreateDb();
        var service = new CompartmentAllocationService(db, new Clock());
        Guid delivery = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        var first = new CompartmentReservation { Id = Guid.NewGuid(), LockerCompartmentId = Guid.NewGuid(), DeliveryRequestId = delivery, ExpiresAt = Now.AddMinutes(1) };
        var untouched = new CompartmentReservation { Id = Guid.NewGuid(), LockerCompartmentId = Guid.NewGuid(), DeliveryRequestId = other, ExpiresAt = Now.AddMinutes(1) };
        db.AddRange(first, untouched);
        await db.SaveChangesAsync();
        await service.ReleaseAsync(delivery, null, Now);
        Assert.Equal(Now, first.ReleasedAt);
        Assert.Null(untouched.ReleasedAt);
        // Release participates in its caller transaction; the caller saves the tracked updates.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(Now, (await db.CompartmentReservations.SingleAsync(r => r.Id == first.Id)).ReleasedAt);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "ALLOC-02")]
    public async Task ReleaseAsync_UTC02()
    {
        await using var db = CreateDb();
        var service = new CompartmentAllocationService(db, new Clock());
        Guid delivery = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        var first = new CompartmentReservation { Id = Guid.NewGuid(), LockerCompartmentId = Guid.NewGuid(), DeliveryRequestId = delivery, ExpiresAt = Now.AddMinutes(1) };
        var untouched = new CompartmentReservation { Id = Guid.NewGuid(), LockerCompartmentId = Guid.NewGuid(), DeliveryRequestId = other, ExpiresAt = Now.AddMinutes(1) };
        db.AddRange(first, untouched);
        await db.SaveChangesAsync();
        await service.ReleaseAsync(Guid.NewGuid(), null, Now);
        Assert.Null(first.ReleasedAt);
        Assert.Null(untouched.ReleasedAt);
        Assert.Equal(2, db.CompartmentReservations.Count());
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    [Trait("Category", "ALLOC-02")]
    public async Task ReleaseAsync_UTC03()
    {
        await using var db = CreateDb();
        var service = new CompartmentAllocationService(db, new Clock());
        Guid delivery = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        var first = new CompartmentReservation { Id = Guid.NewGuid(), LockerCompartmentId = Guid.NewGuid(), DeliveryRequestId = delivery, ExpiresAt = Now.AddMinutes(1) };
        var untouched = new CompartmentReservation { Id = Guid.NewGuid(), LockerCompartmentId = Guid.NewGuid(), DeliveryRequestId = other, ExpiresAt = Now.AddMinutes(1) };
        db.AddRange(first, untouched);
        await db.SaveChangesAsync();
        // The non-nullable timestamp uses default; Excel expects no exception even with both owner IDs present.
        await service.ReleaseAsync(Guid.Empty, Guid.Empty, default);
        Assert.Null(first.ReleasedAt);
        Assert.Null(untouched.ReleasedAt);
        Assert.Empty(db.AuditLogs);
    }


    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static Locker Locker(string code = "L01") => new() { Id = Guid.NewGuid(), Code = code, Address = "Address", RecoveryAddress = "Recovery", DeviceIdentifier = "DEVICE-" + code, OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static LockerCompartment Compartment(Locker locker, string code = "C01") => new() { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = code, HardwareCode = "HW-" + code, HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
}
