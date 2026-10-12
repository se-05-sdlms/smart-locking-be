using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelReminderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var pushService = new RecordingPushService();
        var service = new ParcelReminderService(dbContext, new FixedTimeProvider(Now), pushService);

        var resident = CreateResident();
        var locker = CreateLocker();
        var policy = CreatePolicy();
        var delivery = CreateDelivery(resident, locker, policy);
        var parcel = CreateParcel(delivery, Now.AddDays(-2), Now.AddHours(2), Now.AddDays(3));

        dbContext.AddRange(resident.User, resident, locker, policy, delivery, parcel);
        await dbContext.SaveChangesAsync();

        int result = await service.SendDailyRemindersAsync();

        Assert.Equal(1, result);
        Assert.Single(pushService.EnqueuedIds);
    }

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var pushService = new RecordingPushService();
        var service = new ParcelReminderService(dbContext, new FixedTimeProvider(Now), pushService);

        var locker = CreateLocker();
        var policy = CreatePolicy();
        var delivery = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            ResidentProfileId = null,
            ResidentProfile = null,
            SystemPolicyId = policy.Id,
            SystemPolicy = policy,
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.Deposited,
            LastActivityAt = Now,
            SessionExpiresAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        var parcel = CreateParcel(delivery, Now.AddDays(-2), Now.AddHours(2), Now.AddDays(3));

        dbContext.AddRange(locker, policy, delivery, parcel);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendDailyRemindersAsync());
        Assert.Equal("Parcel does not have a resident owner.", exception.Message);
    }

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var pushService = new RecordingPushService();
        var service = new ParcelReminderService(dbContext, new FixedTimeProvider(Now), pushService);

        var resident = CreateResident();
        var locker = CreateLocker();
        var policy = CreatePolicy();
        var delivery = CreateDelivery(resident, locker, policy);
        // StoredAt is in the future relative to reminder start or reminder already sent
        var parcel = CreateParcel(delivery, Now, Now.AddDays(2), Now.AddDays(5));

        // Add already sent notification today
        var (dayStart, _) = ParcelTimeline.GetLocalDayWindow(Now);
        var existingNotification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = resident.UserId,
            ParcelId = parcel.Id,
            Type = "ParcelPickupReminder",
            Channel = NotificationChannel.InApp,
            Title = "Nhắc nhận",
            Message = "Nhắc nhận kiện",
            DeliveryStatus = NotificationDeliveryStatus.Sent,
            CreatedAt = dayStart.AddHours(1)
        };

        dbContext.AddRange(resident.User, resident, locker, policy, delivery, parcel, existingNotification);
        await dbContext.SaveChangesAsync();

        int result = await service.SendDailyRemindersAsync();

        Assert.Equal(0, result);
        Assert.Empty(pushService.EnqueuedIds);
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ResidentProfile CreateResident()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"resident-{Guid.NewGuid():N}@boxora.local",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        return new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            FullName = "Nguyen Van A"
        };
    }

    private static Locker CreateLocker() => new()
    {
        Id = Guid.NewGuid(),
        Code = "LK-REM-01",
        Address = "Address",
        RecoveryAddress = "Recovery",
        DeviceIdentifier = "DEV-REM-01",
        OperationalStatus = LockerOperationalStatus.Operational,
        ConnectionStatus = LockerConnectionStatus.Online,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static SystemPolicy CreatePolicy() => new()
    {
        Id = Guid.NewGuid(),
        Version = 1,
        OverdueFeePerHour = 5000,
        OverdueStartAfterHours = 48,
        MaxStorageHours = 120,
        PickupReminderStartDay = 1,
        Currency = "vnd",
        CreatedAt = Now
    };

    private static DeliveryRequest CreateDelivery(ResidentProfile resident, Locker locker, SystemPolicy policy) => new()
    {
        Id = Guid.NewGuid(),
        ResidentProfileId = resident.Id,
        ResidentProfile = resident,
        LockerId = locker.Id,
        Locker = locker,
        SystemPolicyId = policy.Id,
        SystemPolicy = policy,
        GuestSessionTokenHash = "hash",
        Status = DeliveryRequestStatus.Deposited,
        LastActivityAt = Now,
        SessionExpiresAt = Now,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static Parcel CreateParcel(DeliveryRequest delivery, DateTimeOffset storedAt, DateTimeOffset pickupDue, DateTimeOffset maxStorage) => new()
    {
        Id = Guid.NewGuid(),
        DeliveryRequestId = delivery.Id,
        DeliveryRequest = delivery,
        ParcelCode = $"P-{Guid.NewGuid():N}",
        Status = ParcelStatus.Stored,
        StoredAt = storedAt,
        PickupDueAt = pickupDue,
        MaxStorageUntil = maxStorage,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private sealed class RecordingPushService : IPushNotificationService
    {
        public List<Guid> EnqueuedIds { get; } = [];

        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => Guid.NewGuid();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => Guid.NewGuid();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => Guid.NewGuid();
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => Guid.NewGuid();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue)
        {
            var id = Guid.NewGuid();
            EnqueuedIds.Add(id);
            return id;
        }
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
