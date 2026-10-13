using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelReminderServiceTests
{
    [Theory]
    [InlineData(24, 1)]
    [InlineData(23, 1)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public async Task ClearanceWarning_IsDistinctFromDailyReminderAndSentOnceAcrossDays(int remainingHours, int expected)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = new DateTimeOffset(2026, 10, 12, 16, 30, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var owner = new User { Id = Guid.NewGuid(), PasswordHash = "test" };
        var resident = new ResidentProfile { Id = Guid.NewGuid(), User = owner, FullName = "Resident" };
        var locker = new Locker { Id = Guid.NewGuid(), Code = "L1", Address = "A", RecoveryAddress = "B", DeviceIdentifier = "test" };
        var policy = new SystemPolicy { Id = Guid.NewGuid(), Currency = "VND", PickupReminderStartDay = 99, OverdueStartAfterHours = 24, MaxStorageHours = 168 };
        var delivery = new DeliveryRequest { Id = Guid.NewGuid(), Locker = locker, ResidentProfile = resident, SystemPolicy = policy, GuestSessionTokenHash = "test" };
        var parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequest = delivery, ParcelCode = "P1", Status = ParcelStatus.Overdue, StoredAt = now.AddDays(-6), PickupDueAt = now.AddDays(-5), MaxStorageUntil = now.AddHours(remainingHours) };
        db.Add(parcel);
        db.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = owner.Id, ParcelId = parcel.Id, Type = "ParcelPickupReminder", Channel = NotificationChannel.InApp, Title = "Reminder", Message = "Reminder", CreatedAt = now.AddMinutes(-10) });
        await db.SaveChangesAsync();
        using var http = new HttpClient(new RejectNetwork());
        var push = new ExpoPushNotificationService(db, http, clock, NullLogger<ExpoPushNotificationService>.Instance);
        Assert.Equal(expected, await new ParcelReminderService(db, clock, push).SendDailyRemindersAsync());
        var warnings = await db.Notifications.Where(n => n.Type == "ParcelClearanceWarning" && n.Channel == NotificationChannel.InApp).ToListAsync();
        Assert.Equal(expected, warnings.Count);
        if (expected == 1) Assert.Contains("24", warnings[0].Message);
        var nextDay = new FixedClock(now.AddHours(1));
        var nextPush = new ExpoPushNotificationService(db, http, nextDay, NullLogger<ExpoPushNotificationService>.Instance);
        Assert.Equal(0, await new ParcelReminderService(db, nextDay, nextPush).SendDailyRemindersAsync());
    }

    [Fact]
    public async Task Reminders_begin_on_configured_day_and_only_enqueue_once_per_day()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.FromHours(7)));
        var locker = new Locker { Id = Guid.NewGuid(), Code = "L1", Address = "A", RecoveryAddress = "B", DeviceIdentifier = "TEST" };
        var owner = new User { Id = Guid.NewGuid(), Role = UserRole.Resident, Status = UserStatus.Active, PasswordHash = "test" };
        var resident = new ResidentProfile { Id = Guid.NewGuid(), User = owner, UserId = owner.Id, FullName = "Resident" };
        var policy = new SystemPolicy { Id = Guid.NewGuid(), Currency = "VND", PickupReminderStartDay = 5, OverdueStartAfterHours = 168, MaxStorageHours = 336 };
        var delivery = new DeliveryRequest { Id = Guid.NewGuid(), Locker = locker, LockerId = locker.Id, ResidentProfile = resident, ResidentProfileId = resident.Id, SystemPolicy = policy, SystemPolicyId = policy.Id, GuestSessionTokenHash = "test" };
        var parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequest = delivery, DeliveryRequestId = delivery.Id, ParcelCode = "P1", Status = ParcelStatus.Stored, StoredAt = clock.GetUtcNow().AddDays(-6), PickupDueAt = clock.GetUtcNow().AddDays(1), MaxStorageUntil = clock.GetUtcNow().AddDays(8) };
        db.Add(parcel);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new RejectNetwork());
        var push = new ExpoPushNotificationService(db, http, clock, NullLogger<ExpoPushNotificationService>.Instance);
        var service = new ParcelReminderService(db, clock, push);
        Assert.Equal(1, await service.SendDailyRemindersAsync());
        Assert.Equal(0, await service.SendDailyRemindersAsync());
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Equal(1, await db.Notifications.CountAsync(item => item.Channel == NotificationChannel.InApp));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No external requests are allowed in this test.");
    }
}
