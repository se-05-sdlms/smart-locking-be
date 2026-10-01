using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class OverdueChargeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CalculateAsync_OverdueParcel_CreatesChargeAndHistory()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Parcel parcel = await SeedParcelAsync(dbContext, Now.AddHours(-2).AddMinutes(-1));
        var service = new OverdueChargeService(dbContext, new FixedTimeProvider(Now));

        int processed = await service.CalculateAsync();

        Assert.Equal(1, processed);
        Assert.Equal(ParcelStatus.Overdue, parcel.Status);
        OverdueCharge charge = await dbContext.OverdueCharges.SingleAsync();
        Assert.Equal(15_000m, charge.Amount);
        Assert.Equal(5_000m, charge.RatePerHourSnapshot);
        Assert.Equal(parcel.PickupDueAt, charge.ChargeStartAt);
        ParcelStatusHistory history = await dbContext.ParcelStatusHistories.SingleAsync();
        Assert.Equal(ParcelStatus.Stored, history.FromStatus);
        Assert.Equal(ParcelStatus.Overdue, history.ToStatus);
    }

    [Fact]
    public async Task CalculateAsync_ExistingCharge_UsesSnapshotAndDoesNotDuplicateHistory()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Parcel parcel = await SeedParcelAsync(dbContext, Now.AddHours(-3), ParcelStatus.Overdue);
        parcel.OverdueCharge = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            Parcel = parcel,
            Amount = 5_000m,
            RatePerHourSnapshot = 5_000m,
            Currency = "VND",
            ChargeStartAt = parcel.PickupDueAt,
            CalculatedThrough = Now.AddHours(-2),
            Status = OverdueChargeStatus.Outstanding,
            CreatedAt = Now.AddHours(-2),
            UpdatedAt = Now.AddHours(-2),
        };
        parcel.DeliveryRequest.SystemPolicy.OverdueFeePerHour = 99_000m;
        dbContext.OverdueCharges.Add(parcel.OverdueCharge);
        await dbContext.SaveChangesAsync();
        var service = new OverdueChargeService(dbContext, new FixedTimeProvider(Now));

        await service.CalculateAsync();

        Assert.Equal(15_000m, parcel.OverdueCharge.Amount);
        Assert.Empty(dbContext.ParcelStatusHistories);
    }

    [Fact]
    public async Task CalculateAsync_PaidCharge_IsNotChanged()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Parcel parcel = await SeedParcelAsync(dbContext, Now.AddHours(-4), ParcelStatus.Overdue);
        parcel.OverdueCharge = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            Parcel = parcel,
            Amount = 10_000m,
            RatePerHourSnapshot = 5_000m,
            Currency = "VND",
            ChargeStartAt = parcel.PickupDueAt,
            CalculatedThrough = Now.AddHours(-2),
            Status = OverdueChargeStatus.Paid,
            CreatedAt = Now.AddHours(-3),
            UpdatedAt = Now.AddHours(-2),
            PaidAt = Now.AddHours(-2),
        };
        dbContext.OverdueCharges.Add(parcel.OverdueCharge);
        await dbContext.SaveChangesAsync();
        var service = new OverdueChargeService(dbContext, new FixedTimeProvider(Now));

        await service.CalculateAsync();

        Assert.Equal(10_000m, parcel.OverdueCharge.Amount);
        Assert.Equal(Now.AddHours(-2), parcel.OverdueCharge.CalculatedThrough);
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Parcel> SeedParcelAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset pickupDueAt,
        ParcelStatus status = ParcelStatus.Stored)
    {
        User administrator = new()
        {
            Id = Guid.NewGuid(),
            Email = "admin@boxora.local",
            PasswordHash = "hash",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        SystemPolicy policy = new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            OverdueFeePerHour = 5_000m,
            Currency = "vnd",
            CreatedByUserId = administrator.Id,
            CreatedByUser = administrator,
            CreatedAt = Now,
        };
        DeliveryRequest delivery = new()
        {
            Id = Guid.NewGuid(),
            LockerId = Guid.NewGuid(),
            SystemPolicyId = policy.Id,
            SystemPolicy = policy,
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.Deposited,
            LastActivityAt = Now,
            SessionExpiresAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Parcel parcel = new()
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = delivery.Id,
            DeliveryRequest = delivery,
            ParcelCode = $"P-{Guid.NewGuid():N}",
            Status = status,
            StoredAt = pickupDueAt.AddHours(-24),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = pickupDueAt.AddDays(3),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        dbContext.AddRange(administrator, policy, delivery, parcel);
        await dbContext.SaveChangesAsync();
        return parcel;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
