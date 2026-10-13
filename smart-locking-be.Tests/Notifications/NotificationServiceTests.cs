using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
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
        Notification unread = CreateNotification(userId, NotificationChannel.InApp, isRead: false);
        Notification read = CreateNotification(userId, NotificationChannel.InApp, isRead: true);
        dbContext.Notifications.AddRange(unread, read);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        PagedResult<NotificationResponse> result = await service.GetForUserAsync(userId, false, 20, default, 1);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification unread = CreateNotification(userId, NotificationChannel.InApp, isRead: false);
        Notification read = CreateNotification(userId, NotificationChannel.InApp, isRead: true);
        dbContext.Notifications.AddRange(unread, read);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        PagedResult<NotificationResponse> result = await service.GetForUserAsync(userId, true, 20, default, 1);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.False(result.Items[0].IsRead);
    }

    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetForUserAsync(Guid.NewGuid(), false, 0, default, 1));

        Assert.Contains("Limit must be between 1 and 100", ex.Message);
    }

    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetForUserAsync(Guid.NewGuid(), false, 101, default, 1));

        Assert.Contains("Limit must be between 1 and 100", ex.Message);
    }

    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        dbContext.Notifications.AddRange(
            CreateNotification(userId, NotificationChannel.InApp, isRead: false),
            CreateNotification(userId, NotificationChannel.InApp, isRead: true));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        PagedResult<NotificationResponse> result = await service.GetForUserAsync(userId, false, 1, default, 1);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
    }

    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        var service = CreateService(dbContext);

        PagedResult<NotificationResponse> result = await service.GetForUserAsync(userId, false, 20, default, 0);

        Assert.Equal(1, result.PageNumber);
    }
    #endregion

    #region NOT-02: SetReadStateAsync
    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task SetReadStateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification notification = CreateNotification(userId, NotificationChannel.InApp, isRead: false);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        NotificationResponse response = await CreateService(dbContext)
            .SetReadStateAsync(userId, notification.Id, true, default);

        Assert.True(response.IsRead);
        Assert.NotNull(response.ReadAt);
        Assert.True(notification.IsRead);
    }

    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task SetReadStateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification notification = CreateNotification(userId, NotificationChannel.InApp, isRead: true);
        notification.ReadAt = DateTimeOffset.UtcNow;
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        NotificationResponse response = await CreateService(dbContext)
            .SetReadStateAsync(userId, notification.Id, false, default);

        Assert.False(response.IsRead);
        Assert.Null(response.ReadAt);
        Assert.False(notification.IsRead);
    }

    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task SetReadStateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        DateTimeOffset readAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        Notification notification = CreateNotification(userId, NotificationChannel.InApp, isRead: true);
        notification.ReadAt = readAt;
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        NotificationResponse response = await CreateService(dbContext)
            .SetReadStateAsync(userId, notification.Id, true, default);

        Assert.True(response.IsRead);
        Assert.Equal(readAt, notification.ReadAt);
    }

    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task SetReadStateAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification pushNotif = CreateNotification(userId, NotificationChannel.Push, isRead: false);
        dbContext.Notifications.Add(pushNotif);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SetReadStateAsync(userId, pushNotif.Id, true, default));

        Assert.Equal("Notification not found.", ex.Message);
    }
    #endregion

    #region NOT-03: MarkAllReadAsync
    [Fact]
    [Trait("Category", "NOT-03")]
    public async Task MarkAllReadAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        Guid userId = Guid.NewGuid();
        Notification first = CreateNotification(userId, NotificationChannel.InApp, isRead: false);
        Notification second = CreateNotification(userId, NotificationChannel.InApp, isRead: false);
        Notification push = CreateNotification(userId, NotificationChannel.Push, isRead: false);
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

    private static Notification CreateNotification(Guid userId, NotificationChannel channel, bool isRead = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Channel = channel,
            Type = "DeliveryApprovalRequest",
            Title = "Yêu cầu giao hàng",
            Message = "Shipper đang chờ bạn xác nhận",
            DeliveryStatus = NotificationDeliveryStatus.Pending,
            IsRead = isRead,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
