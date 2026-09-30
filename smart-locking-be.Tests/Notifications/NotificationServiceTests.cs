using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Notifications;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task GetForUserAsync_ReturnsOnlyOwnedInAppNotifications()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification owned = CreateNotification(userId, NotificationChannel.InApp);
        dbContext.Notifications.AddRange(
            owned,
            CreateNotification(userId, NotificationChannel.Push),
            CreateNotification(Guid.NewGuid(), NotificationChannel.InApp));
        await dbContext.SaveChangesAsync();

        IReadOnlyCollection<NotificationResponse> result = await CreateService(dbContext)
            .GetForUserAsync(userId, false, 50, default);

        Assert.Equal(owned.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task MarkReadAsync_IsIdempotentAndSetsTimestamp()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification notification = CreateNotification(userId, NotificationChannel.InApp);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        NotificationResponse first = await CreateService(dbContext)
            .MarkReadAsync(userId, notification.Id, default);
        NotificationResponse second = await CreateService(dbContext)
            .MarkReadAsync(userId, notification.Id, default);

        Assert.True(first.IsRead);
        Assert.NotNull(first.ReadAt);
        Assert.Equal(first.ReadAt, second.ReadAt);
    }

    [Fact]
    public async Task MarkReadAsync_ForAnotherUser_ReturnsNotFound()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Notification notification = CreateNotification(Guid.NewGuid(), NotificationChannel.InApp);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(dbContext)
            .MarkReadAsync(Guid.NewGuid(), notification.Id, default));
    }

    [Fact]
    public async Task MarkAllReadAsync_UpdatesOnlyUnreadInAppNotifications()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification first = CreateNotification(userId, NotificationChannel.InApp);
        Notification second = CreateNotification(userId, NotificationChannel.InApp);
        Notification push = CreateNotification(userId, NotificationChannel.Push);
        dbContext.Notifications.AddRange(first, second, push);
        await dbContext.SaveChangesAsync();

        MarkAllNotificationsReadResponse result = await CreateService(dbContext)
            .MarkAllReadAsync(userId, default);

        Assert.Equal(2, result.UpdatedCount);
        Assert.True(first.IsRead);
        Assert.True(second.IsRead);
        Assert.False(push.IsRead);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static NotificationService CreateService(ApplicationDbContext dbContext) =>
        new(dbContext, TimeProvider.System);

    private static Notification CreateNotification(Guid userId, NotificationChannel channel) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = "ParcelStored",
        Channel = channel,
        Title = "Bưu kiện mới",
        Message = "Bưu kiện đã được gửi vào locker.",
        DeliveryStatus = NotificationDeliveryStatus.Sent,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
