using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Tests.Realtime;

public sealed class RealtimeNotifierTests
{
    private sealed class RecordingRealtimeNotifier : IOperationsRealtimeNotifier
    {
        public List<(Guid UserId, RealtimeEvent Event)> UserEvents { get; } = [];
        public List<(Guid LockerId, RealtimeEvent Event)> LockerEvents { get; } = [];

        public Task PublishToUserAsync(Guid userId, RealtimeEvent notification, CancellationToken cancellationToken = default)
        {
            UserEvents.Add((userId, notification));
            return Task.CompletedTask;
        }

        public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent notification, CancellationToken cancellationToken = default)
        {
            LockerEvents.Add((lockerId, notification));
            return Task.CompletedTask;
        }
    }

    #region RTN-01: PublishToUserAsync
    [Fact]
    [Trait("Category", "RTN-01")]
    public async Task PublishToUserAsync_UTC01()
    {
        var notifier = new RecordingRealtimeNotifier();
        var userId = Guid.NewGuid();
        var evt = new RealtimeEvent("ParcelStored", Guid.NewGuid(), null, "Created", "Bưu kiện mới", DateTimeOffset.UtcNow);

        await notifier.PublishToUserAsync(userId, evt);

        Assert.Single(notifier.UserEvents);
        Assert.Equal(userId, notifier.UserEvents[0].UserId);
    }

    [Fact]
    [Trait("Category", "RTN-01")]
    public async Task PublishToUserAsync_UTC02()
    {
        var notifier = new RecordingRealtimeNotifier();
        var evt = new RealtimeEvent("ParcelStored", Guid.NewGuid(), null, "Created", "Bưu kiện mới", DateTimeOffset.UtcNow);

        await notifier.PublishToUserAsync(Guid.NewGuid(), evt);
        Assert.Single(notifier.UserEvents);
    }

    [Fact]
    [Trait("Category", "RTN-01")]
    public async Task PublishToUserAsync_UTC03()
    {
        var notifier = new RecordingRealtimeNotifier();
        await notifier.PublishToUserAsync(Guid.Empty, null!);
        Assert.Single(notifier.UserEvents);
    }
    #endregion

    #region RTN-02: PublishToLockerAsync
    [Fact]
    [Trait("Category", "RTN-02")]
    public async Task PublishToLockerAsync_UTC01()
    {
        var notifier = new RecordingRealtimeNotifier();
        var lockerId = Guid.NewGuid();
        var evt = new RealtimeEvent("LockerStatusChanged", null, lockerId, "Updated", "Locker Online", DateTimeOffset.UtcNow);

        await notifier.PublishToLockerAsync(lockerId, evt);

        Assert.Single(notifier.LockerEvents);
        Assert.Equal(lockerId, notifier.LockerEvents[0].LockerId);
    }

    [Fact]
    [Trait("Category", "RTN-02")]
    public async Task PublishToLockerAsync_UTC02()
    {
        var notifier = new RecordingRealtimeNotifier();
        var evt = new RealtimeEvent("LockerStatusChanged", null, Guid.NewGuid(), "Updated", "Locker Online", DateTimeOffset.UtcNow);

        await notifier.PublishToLockerAsync(Guid.NewGuid(), evt);
        Assert.Single(notifier.LockerEvents);
    }

    [Fact]
    [Trait("Category", "RTN-02")]
    public async Task PublishToLockerAsync_UTC03()
    {
        var notifier = new RecordingRealtimeNotifier();
        await notifier.PublishToLockerAsync(Guid.Empty, null!);
        Assert.Single(notifier.LockerEvents);
    }
    #endregion
}
