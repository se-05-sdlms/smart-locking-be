using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class NotificationServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "NOTIFICATION")]
    public async Task GetForUserAsync_ReturnsUnreadNotificationsAndMarkAllReadUpdatesOwnerOnly()
    {
        await using var db = CreateDbContext();
        Guid userId = Guid.NewGuid();
        db.Notifications.AddRange(
            Notification(userId, "new", false, Now.AddMinutes(1)),
            Notification(userId, "old", true, Now),
            Notification(Guid.NewGuid(), "other", false, Now.AddMinutes(2)));
        await db.SaveChangesAsync();
        var service = new NotificationService(db, new FixedTimeProvider(Now.AddHours(1)));

        var unread = await service.GetForUserAsync(userId, true, 10, default);
        var result = await service.MarkAllReadAsync(userId, default);

        Assert.Single(unread);
        Assert.Equal("new", unread.Single().Type);
        Assert.Equal(1, result.UpdatedCount);
        Assert.All(db.Notifications.Where(item => item.UserId == userId), item => Assert.True(item.IsRead));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [Trait("Category", "NOTIFICATION")]
    public async Task GetForUserAsync_RejectsInvalidLimit(int limit)
    {
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<BadRequestException>(() => new NotificationService(db, TimeProvider.System)
            .GetForUserAsync(Guid.NewGuid(), false, limit, default));
    }

    [Fact]
    [Trait("Category", "NOTIFICATION")]
    public async Task MarkReadAsync_RequiresOwningUser()
    {
        await using var db = CreateDbContext();
        var notification = Notification(Guid.NewGuid(), "new", false, Now);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => new NotificationService(db, TimeProvider.System)
            .MarkReadAsync(Guid.NewGuid(), notification.Id, default));
    }

    private static ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Notification Notification(Guid userId, string type, bool read, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = type,
        Title = type,
        Message = type,
        Channel = NotificationChannel.InApp,
        DeliveryStatus = NotificationDeliveryStatus.Sent,
        IsRead = read,
        ReadAt = read ? createdAt : null,
        CreatedAt = createdAt
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
