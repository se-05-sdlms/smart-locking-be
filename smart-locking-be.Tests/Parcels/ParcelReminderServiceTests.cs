using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelReminderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_UTC01()
    {
        await using var db = CreateDb();
        var parcel = SeedParcel(db);
        await db.SaveChangesAsync();
        var push = new RecordingPush(db);
        var service = new ParcelReminderService(db, new Clock(), push);

        Assert.Equal(1, await service.SendDailyRemindersAsync());
        var call = Assert.Single(push.Calls);
        Assert.Equal((parcel.DeliveryRequest.ResidentProfile!.UserId, parcel.DeliveryRequestId, parcel.Id, "L01", 3, false), call);
        Assert.Equal(push.EnqueuedId, Assert.Single(push.Sent));
        Assert.True(push.SavedBeforeSend);
        db.ChangeTracker.Clear();
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(ParcelStatus.Stored, (await db.Parcels.SingleAsync()).Status);
        // The reminder already sent today must not be enqueued again.
        Assert.Equal(0, await service.SendDailyRemindersAsync());
        Assert.Single(push.Calls);
        Assert.Single(push.Sent);
    }

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_UTC02()
    {
        await using var db = CreateDb();
        var parcel = SeedParcel(db, owner: false);
        await db.SaveChangesAsync();
        var push = new RecordingPush(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new ParcelReminderService(db, new Clock(), push).SendDailyRemindersAsync());
        Assert.Equal("Parcel does not have a resident owner.", ex.Message);
        Assert.Empty(push.Calls);
        Assert.Empty(push.Sent);
        Assert.Empty(db.Notifications);
        Assert.Equal(ParcelStatus.Stored, parcel.Status);
    }

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_UTC03()
    {
        // Excel supplies no method inputs and no concrete expected value. Exercise the no-op state.
        await using var db = CreateDb();
        var push = new RecordingPush(db);
        Assert.Equal(0, await new ParcelReminderService(db, new Clock(), push).SendDailyRemindersAsync());
        Assert.Empty(push.Calls);
        Assert.Empty(push.Sent);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_FiltersIneligibleParcelsAndUsesVietnamDay()
    {
        await using var db = CreateDb();
        var fresh = SeedParcel(db); fresh.StoredAt = Now;
        var retrieved = SeedParcel(db); retrieved.Status = ParcelStatus.Retrieved;
        var expired = SeedParcel(db); expired.MaxStorageUntil = Now;
        var alreadySent = SeedParcel(db);
        db.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = alreadySent.DeliveryRequest.ResidentProfile!.UserId, ParcelId = alreadySent.Id, Type = "ParcelPickupReminder", Channel = NotificationChannel.InApp, CreatedAt = new DateTimeOffset(2026, 10, 14, 17, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();
        var push = new RecordingPush(db);
        Assert.Equal(0, await new ParcelReminderService(db, new Clock(), push).SendDailyRemindersAsync());
        Assert.Empty(push.Calls);
        Assert.Single(db.Notifications);
    }

    [Fact]
    [Trait("Category", "REM-01")]
    public async Task SendDailyRemindersAsync_SendsOverdueWarningAt24Hours()
    {
        await using var db = CreateDb();
        var parcel = SeedParcel(db); parcel.Status = ParcelStatus.Overdue; parcel.PickupDueAt = Now; parcel.MaxStorageUntil = Now.AddHours(24);
        await db.SaveChangesAsync();
        var push = new RecordingPush(db);
        Assert.Equal(1, await new ParcelReminderService(db, new Clock(), push).SendDailyRemindersAsync());
        var call = Assert.Single(push.Calls);
        Assert.Equal(1, call.Days);
        Assert.True(call.Overdue);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Parcel SeedParcel(ApplicationDbContext db, bool owner = true)
    {
        var locker = new Locker { Id = Guid.NewGuid(), Code = "L01", Address = "Address", DeviceIdentifier = Guid.NewGuid().ToString() };
        var policy = new SystemPolicy { Id = Guid.NewGuid(), Version = 1, Currency = "VND", PickupReminderStartDay = 1 };
        var resident = owner ? new ResidentProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), FullName = "Resident" } : null;
        var request = new DeliveryRequest { Id = Guid.NewGuid(), Locker = locker, SystemPolicy = policy, ResidentProfile = resident, GuestSessionTokenHash = "hash" };
        var parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequest = request, ParcelCode = Guid.NewGuid().ToString(), Status = ParcelStatus.Stored, StoredAt = Now.AddDays(-2), PickupDueAt = Now.AddDays(1), MaxStorageUntil = Now.AddDays(3) };
        db.Add(parcel);
        return parcel;
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class RecordingPush(ApplicationDbContext db) : IPushNotificationService
    {
        public Guid EnqueuedId { get; } = Guid.NewGuid();
        public List<(Guid User, Guid Request, Guid Parcel, string Locker, int Days, bool Overdue)> Calls { get; } = [];
        public List<Guid> Sent { get; } = [];
        public bool SavedBeforeSend { get; private set; }
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue)
        {
            Calls.Add((residentUserId, deliveryRequestId, parcelId, lockerCode, daysUntilTransfer, isOverdue));
            db.Notifications.Add(new Notification { Id = EnqueuedId, UserId = residentUserId, ParcelId = parcelId, Type = "ParcelPickupReminder", Channel = NotificationChannel.InApp, CreatedAt = Now });
            return EnqueuedId;
        }
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default)
        {
            SavedBeforeSend = db.ChangeTracker.Entries<Notification>().Single(n => n.Entity.Id == notificationId).State == EntityState.Unchanged;
            Sent.Add(notificationId);
            return Task.CompletedTask;
        }
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => throw new NotSupportedException();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => throw new NotSupportedException();
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => throw new NotSupportedException();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => throw new NotSupportedException();
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
