using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Notifications;

public sealed class NotificationServiceTests
{
    #region NOT-01: GetForUserAsync
    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification owned = CreateNotification(userId, NotificationChannel.InApp);
        dbContext.Notifications.AddRange(
            owned,
            CreateNotification(userId, NotificationChannel.Push),
            CreateNotification(Guid.NewGuid(), NotificationChannel.InApp));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext)
            .GetForUserAsync(userId, false, 50, default);

        Assert.Equal(owned.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetForUserAsync(userId, false, 0, default));

        Assert.Equal("Limit must be between 1 and 100. (Parameter 'limit')", ex.Message);
    }

    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var result = await service.GetForUserAsync(Guid.Empty, false, 10, default);
        Assert.Empty(result.Items);
    }
    #endregion

    #region NOT-02: SetReadStateAsync
    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task SetReadStateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification notification = CreateNotification(userId, NotificationChannel.InApp);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        NotificationResponse first = await CreateService(dbContext)
            .SetReadStateAsync(userId, notification.Id, true, default);
        NotificationResponse second = await CreateService(dbContext)
            .SetReadStateAsync(userId, notification.Id, true, default);

        Assert.True(first.IsRead);
        Assert.NotNull(first.ReadAt);
        Assert.Equal(first.ReadAt, second.ReadAt);
    }

    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task SetReadStateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Notification notification = CreateNotification(Guid.NewGuid(), NotificationChannel.InApp);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(dbContext)
            .SetReadStateAsync(Guid.NewGuid(), notification.Id, true, default));

        Assert.Equal("Notification not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task SetReadStateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() =>
            service.SetReadStateAsync(Guid.Empty, Guid.Empty, false, default));
    }
    #endregion

    #region NOT-03: MarkAllReadAsync
    [Fact]
    [Trait("Category", "NOT-03")]
    public async Task MarkAllReadAsync_UTC01()
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

    [Fact]
    [Trait("Category", "NOT-03")]
    public async Task MarkAllReadAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        MarkAllNotificationsReadResponse result = await service.MarkAllReadAsync(Guid.NewGuid(), default);
        Assert.Equal(0, result.UpdatedCount);
    }

    [Fact]
    [Trait("Category", "NOT-03")]
    public async Task MarkAllReadAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        MarkAllNotificationsReadResponse result = await service.MarkAllReadAsync(Guid.Empty, default);
        Assert.Equal(0, result.UpdatedCount);
    }
    #endregion

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
