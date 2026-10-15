using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class OverdueChargeServiceTests
{
    [Fact]
    [Trait("Category", "ODC-01")]
    public async Task CalculateAsync_UTC01()
    {
        await using var db = CreateDb();
        var policy = Policy();
        var locker = Locker();
        var request = new DeliveryRequest { Id = Guid.NewGuid(), Locker = locker, LockerId = locker.Id, SystemPolicy = policy, SystemPolicyId = policy.Id, GuestSessionTokenHash = "hash" };
        var parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequest = request, DeliveryRequestId = request.Id, ParcelCode = "P1", Status = ParcelStatus.Stored, PickupDueAt = Now.AddMinutes(-61), MaxStorageUntil = Now.AddDays(1) };
        db.AddRange(policy, locker, request, parcel);
        await db.SaveChangesAsync();
        int count = await new OverdueChargeService(db, new Clock()).CalculateAsync();
        Assert.Equal(1, count);
        db.ChangeTracker.Clear();
        Assert.Equal(ParcelStatus.Overdue, (await db.Parcels.SingleAsync()).Status);
        var charge = Assert.Single(db.OverdueCharges);
        Assert.Equal(10000m, charge.Amount);
        Assert.Equal(5000m, charge.RatePerHourSnapshot);
        Assert.Equal("VND", charge.Currency);
        Assert.Equal(OverdueChargeStatus.Outstanding, charge.Status);
        Assert.Equal(Now, charge.CalculatedThrough);
        var history = Assert.Single(db.ParcelStatusHistories);
        Assert.Equal(parcel.Id, history.ParcelId);
        Assert.Equal(ParcelStatus.Stored, history.FromStatus);
        Assert.Equal(ParcelStatus.Overdue, history.ToStatus);
    }

    [Fact]
    [Trait("Category", "ODC-01")]
    public async Task CalculateAsync_UTC02()
    {
        await using var db = CreateDb();
        Assert.Equal(0, await new OverdueChargeService(db, new Clock()).CalculateAsync());
        Assert.Empty(db.OverdueCharges);
        Assert.Empty(db.ParcelStatusHistories);
    }

    [Fact]
    [Trait("Category", "ODC-01")]
    public async Task CalculateAsync_UTC03()
    {
        await using var db = CreateDb();
        var policy = Policy();
        var locker = Locker();
        var request = new DeliveryRequest { Id = Guid.NewGuid(), Locker = locker, LockerId = locker.Id, SystemPolicy = policy, SystemPolicyId = policy.Id, GuestSessionTokenHash = "hash" };
        var parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequest = request, DeliveryRequestId = request.Id, ParcelCode = "P1", Status = ParcelStatus.Stored, PickupDueAt = Now, MaxStorageUntil = Now.AddDays(1) };
        db.AddRange(policy, locker, request, parcel);
        await db.SaveChangesAsync();
        Assert.Equal(0, await new OverdueChargeService(db, new Clock()).CalculateAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(ParcelStatus.Stored, (await db.Parcels.SingleAsync()).Status);
        Assert.Empty(db.OverdueCharges);
        Assert.Empty(db.ParcelStatusHistories);
    }


    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static Locker Locker(string code = "L01") => new() { Id = Guid.NewGuid(), Code = code, Address = "Address", RecoveryAddress = "Recovery", DeviceIdentifier = "DEVICE-" + code, OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static SystemPolicy Policy(int version = 1, bool active = false) => new() { Id = Guid.NewGuid(), Version = version, IsActive = active, Currency = "VND", CreatedByUserId = Guid.NewGuid(), EffectiveFrom = Now.AddDays(-1), OverdueStartAfterHours = 168, MaxStorageHours = 336, OverdueFeePerHour = 5000, EnableRemoteUnlock = true };
}
