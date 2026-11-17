using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Services;

public sealed class OverdueChargeServiceTests
{
    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

        public void Set(DateTimeOffset newUtcNow) => _utcNow = newUtcNow;
    }

    private static ApplicationDbContext CreateDatabase() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DeliveryRequest Request, SystemPolicy Policy) CreateDeliveryRequest(
        decimal feePerHour = 5000m,
        string currency = "vnd")
    {
        var policy = new SystemPolicy
        {
            Id = Guid.NewGuid(),
            OverdueFeePerHour = feePerHour,
            Currency = currency,
            OverdueStartAfterHours = 24,
            MaxStorageHours = 72
        };

        var request = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            SystemPolicy = policy,
            SystemPolicyId = policy.Id,
            LockerId = Guid.NewGuid()
        };

        return (request, policy);
    }

    [Fact]
    public async Task CalculateAsync_StoredParcelBecomesOverdue_CalculatesCeiledHoursAndCreatesOutstandingCharge()
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 5000m, currency: "vnd");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var pickupDueAt = baseTime;
        var maxStorageUntil = baseTime.AddHours(48);
        // 1 hour and 15 minutes overdue -> TotalHours = 1.25 -> Math.Ceiling = 2 billable hours
        var now = baseTime.AddHours(1).AddMinutes(15);

        var timeProvider = new TestTimeProvider(now);

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-001",
            Status = ParcelStatus.Stored,
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = maxStorageUntil,
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(-24)
        };

        db.AddRange(policy, request, parcel);
        await db.SaveChangesAsync();

        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(1, processed);

        var updatedParcel = await db.Parcels
            .Include(p => p.OverdueCharge)
            .SingleAsync(p => p.Id == parcel.Id);

        Assert.Equal(ParcelStatus.Overdue, updatedParcel.Status);
        Assert.Equal(now, updatedParcel.UpdatedAt);

        // Status history verification
        var history = Assert.Single(db.ParcelStatusHistories);
        Assert.Equal(parcel.Id, history.ParcelId);
        Assert.Equal(ParcelStatus.Stored, history.FromStatus);
        Assert.Equal(ParcelStatus.Overdue, history.ToStatus);
        Assert.Equal("Free storage period expired.", history.Reason);
        Assert.Equal(now, history.ChangedAt);

        // Overdue charge verification
        var charge = updatedParcel.OverdueCharge;
        Assert.NotNull(charge);
        Assert.Equal(10000m, charge.Amount); // 2 hours * 5000
        Assert.Equal(5000m, charge.RatePerHourSnapshot);
        Assert.Equal("VND", charge.Currency);
        Assert.Equal(pickupDueAt, charge.ChargeStartAt);
        Assert.Equal(now, charge.CalculatedThrough);
        Assert.Equal(OverdueChargeStatus.Outstanding, charge.Status);
        Assert.Equal(now, charge.CreatedAt);
        Assert.Equal(now, charge.UpdatedAt);
    }

    [Fact]
    public async Task CalculateAsync_WhenNowExceedsMaxStorageUntil_CapsBillableHoursAtMaxStorageUntil()
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 10000m, currency: "VND");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var pickupDueAt = baseTime;
        var maxStorageUntil = baseTime.AddHours(5); // Capped at 5 hours
        var now = baseTime.AddHours(12); // Now is 12 hours later

        var timeProvider = new TestTimeProvider(now);

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-CAPPED",
            Status = ParcelStatus.Stored,
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = maxStorageUntil,
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(-24)
        };

        db.AddRange(policy, request, parcel);
        await db.SaveChangesAsync();

        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(1, processed);

        var updatedParcel = await db.Parcels
            .Include(p => p.OverdueCharge)
            .SingleAsync(p => p.Id == parcel.Id);

        var charge = updatedParcel.OverdueCharge;
        Assert.NotNull(charge);
        // Billable hours capped at 5 hours (maxStorageUntil - pickupDueAt), not 12 hours
        Assert.Equal(50000m, charge.Amount); // 5 * 10000
        Assert.Equal(maxStorageUntil, charge.CalculatedThrough);
    }

    [Fact]
    public async Task CalculateAsync_ExistingOverdueCharge_UpdatesAmountAndCalculatedThrough()
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 5000m, currency: "VND");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var pickupDueAt = baseTime;
        var maxStorageUntil = baseTime.AddHours(48);

        var existingCharge = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            Amount = 5000m, // initially 1 hour calculated
            RatePerHourSnapshot = 5000m,
            Currency = "VND",
            ChargeStartAt = pickupDueAt,
            CalculatedThrough = baseTime.AddHours(1),
            Status = OverdueChargeStatus.Outstanding,
            CreatedAt = baseTime.AddHours(1),
            UpdatedAt = baseTime.AddHours(1)
        };

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-EXISTING",
            Status = ParcelStatus.Overdue, // Already overdue
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = maxStorageUntil,
            OverdueCharge = existingCharge,
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(1)
        };
        existingCharge.ParcelId = parcel.Id;

        db.AddRange(policy, request, parcel, existingCharge);
        await db.SaveChangesAsync();

        // 3 hours past pickupDueAt
        var now = baseTime.AddHours(3);
        var timeProvider = new TestTimeProvider(now);

        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(1, processed);

        var updatedCharge = await db.OverdueCharges.SingleAsync(c => c.Id == existingCharge.Id);
        Assert.Equal(15000m, updatedCharge.Amount); // 3 * 5000
        Assert.Equal(now, updatedCharge.CalculatedThrough);
        Assert.Equal(now, updatedCharge.UpdatedAt);

        // Parcel status was already Overdue, so no new status history entry
        Assert.Empty(db.ParcelStatusHistories);
    }

    [Fact]
    public async Task CalculateAsync_PaidOverdueCharge_DoesNotModifyAmount()
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 5000m, currency: "VND");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var pickupDueAt = baseTime;
        var maxStorageUntil = baseTime.AddHours(48);

        var existingCharge = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            Amount = 10000m,
            RatePerHourSnapshot = 5000m,
            Currency = "VND",
            ChargeStartAt = pickupDueAt,
            CalculatedThrough = baseTime.AddHours(2),
            Status = OverdueChargeStatus.Paid, // Already paid
            PaidAt = baseTime.AddHours(2),
            CreatedAt = baseTime.AddHours(2),
            UpdatedAt = baseTime.AddHours(2)
        };

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-PAID",
            Status = ParcelStatus.Overdue,
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = maxStorageUntil,
            OverdueCharge = existingCharge,
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(2)
        };
        existingCharge.ParcelId = parcel.Id;

        db.AddRange(policy, request, parcel, existingCharge);
        await db.SaveChangesAsync();

        var now = baseTime.AddHours(5);
        var timeProvider = new TestTimeProvider(now);

        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(1, processed);

        var updatedCharge = await db.OverdueCharges.SingleAsync(c => c.Id == existingCharge.Id);
        Assert.Equal(10000m, updatedCharge.Amount); // Unchanged
        Assert.Equal(baseTime.AddHours(2), updatedCharge.CalculatedThrough); // Unchanged
    }

    [Fact]
    public async Task CalculateAsync_PendingPaymentTransaction_DoesNotModifyAmount()
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 5000m, currency: "VND");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var pickupDueAt = baseTime;
        var maxStorageUntil = baseTime.AddHours(48);

        var existingCharge = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            Amount = 10000m,
            RatePerHourSnapshot = 5000m,
            Currency = "VND",
            ChargeStartAt = pickupDueAt,
            CalculatedThrough = baseTime.AddHours(2),
            Status = OverdueChargeStatus.Outstanding,
            CreatedAt = baseTime.AddHours(2),
            UpdatedAt = baseTime.AddHours(2)
        };

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-PENDING-TX",
            Status = ParcelStatus.Overdue,
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = maxStorageUntil,
            OverdueCharge = existingCharge,
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(2)
        };
        existingCharge.ParcelId = parcel.Id;

        var pendingTx = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OverdueChargeId = existingCharge.Id,
            Amount = 10000m,
            Currency = "VND",
            Status = PaymentTransactionStatus.Pending
        };

        db.AddRange(policy, request, parcel, existingCharge, pendingTx);
        await db.SaveChangesAsync();

        var now = baseTime.AddHours(6);
        var timeProvider = new TestTimeProvider(now);

        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(1, processed);

        var updatedCharge = await db.OverdueCharges.SingleAsync(c => c.Id == existingCharge.Id);
        Assert.Equal(10000m, updatedCharge.Amount); // Unchanged due to pending transaction
        Assert.Equal(baseTime.AddHours(2), updatedCharge.CalculatedThrough); // Unchanged
    }

    [Fact]
    public async Task CalculateAsync_ZeroOverdueFeePerHour_TransitionsStatusToOverdueWithoutCreatingCharge()
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 0m, currency: "VND");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var pickupDueAt = baseTime;
        var maxStorageUntil = baseTime.AddHours(48);
        var now = baseTime.AddHours(2);

        var timeProvider = new TestTimeProvider(now);

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-ZERO-FEE",
            Status = ParcelStatus.Stored,
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = maxStorageUntil,
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(-24)
        };

        db.AddRange(policy, request, parcel);
        await db.SaveChangesAsync();

        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(1, processed);

        var updatedParcel = await db.Parcels
            .Include(p => p.OverdueCharge)
            .SingleAsync(p => p.Id == parcel.Id);

        Assert.Equal(ParcelStatus.Overdue, updatedParcel.Status);
        Assert.Null(updatedParcel.OverdueCharge); // No charge created because fee = 0
        Assert.Single(db.ParcelStatusHistories); // Status history still created
    }

    [Theory]
    [InlineData(-1, "VND")]
    [InlineData(-5000, "VND")]
    [InlineData(5000, "")]
    [InlineData(5000, "   ")]
    [InlineData(5000, null)]
    public async Task CalculateAsync_NegativeFeeOrInvalidCurrency_SkipsParcel(decimal feePerHour, string? currency)
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: feePerHour, currency: currency ?? string.Empty);

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var now = baseTime.AddHours(3);

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-INVALID-POLICY",
            Status = ParcelStatus.Stored,
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = baseTime,
            MaxStorageUntil = baseTime.AddHours(48),
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(-24)
        };

        db.AddRange(policy, request, parcel);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(now);
        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(0, processed);

        var storedParcel = await db.Parcels.SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Stored, storedParcel.Status); // Unchanged
        Assert.Null(storedParcel.OverdueCharge);
        Assert.Empty(db.ParcelStatusHistories);
    }

    [Fact]
    public async Task CalculateAsync_ParcelNotYetDue_IsIgnored()
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 5000m, currency: "VND");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        // Due 2 hours in the future
        var pickupDueAt = baseTime.AddHours(2);
        var now = baseTime;

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-NOT-YET-DUE",
            Status = ParcelStatus.Stored,
            StoredAt = baseTime.AddHours(-10),
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = baseTime.AddHours(48),
            CreatedAt = baseTime.AddHours(-10),
            UpdatedAt = baseTime.AddHours(-10)
        };

        db.AddRange(policy, request, parcel);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(now);
        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(0, processed);

        var storedParcel = await db.Parcels.SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Stored, storedParcel.Status);
        Assert.Null(storedParcel.OverdueCharge);
    }

    [Theory]
    [InlineData(ParcelStatus.Retrieved)]
    [InlineData(ParcelStatus.Removed)]
    public async Task CalculateAsync_NonStoredNonOverdueStatus_IsIgnored(ParcelStatus status)
    {
        await using var db = CreateDatabase();
        var (request, policy) = CreateDeliveryRequest(feePerHour: 5000m, currency: "VND");

        var baseTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var now = baseTime.AddHours(5);

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequest = request,
            DeliveryRequestId = request.Id,
            ParcelCode = "PARCEL-COMPLETED",
            Status = status,
            StoredAt = baseTime.AddHours(-24),
            PickupDueAt = baseTime,
            MaxStorageUntil = baseTime.AddHours(48),
            CreatedAt = baseTime.AddHours(-24),
            UpdatedAt = baseTime.AddHours(-24)
        };

        db.AddRange(policy, request, parcel);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(now);
        var service = new OverdueChargeService(db, timeProvider);

        int processed = await service.CalculateAsync();

        Assert.Equal(0, processed);
        Assert.Empty(db.ParcelStatusHistories);
    }
}
