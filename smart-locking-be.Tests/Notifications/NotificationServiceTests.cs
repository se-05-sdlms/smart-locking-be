using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Notifications;

public sealed class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC01()
    {
        // Excel lists offset/paged output; dev has only limit and an IReadOnlyCollection.
        await using var c = new Context(); var old = c.Seed(); old.CreatedAt = Now.AddHours(-1); var newest = c.Seed(); var read = c.Seed(); read.IsRead = true; c.Seed(Guid.NewGuid()); var push = c.Seed(); push.Channel = NotificationChannel.Push; await c.Db.SaveChangesAsync();
        var result = await c.Service.GetForUserAsync(c.UserId, true, 1, default);
        var item = Assert.Single(result); Assert.Equal(newest.Id, item.Id); Assert.Equal("ParcelStored", item.Type); Assert.Equal("Stored", item.Title); Assert.False(item.IsRead); Assert.Null(item.ReadAt); Assert.Equal(Now, item.CreatedAt);
    }
    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC02()
    { await using var c = new Context(); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.GetForUserAsync(c.UserId, true, 101, default)); Assert.StartsWith("Limit must be between 1 and 100.", ex.Message); }
    [Fact(Skip = "Excel NOT-01 UTC03 gives zero limit but no expected exception; dev requires 1..100. Also no offset parameter exists.")]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_UTC03()
    { await using var c = new Context(); Assert.Null(await c.Service.GetForUserAsync(Guid.Empty, false, 0, default)); }

    [Fact(Skip = "Excel NOT-02 requires SetReadStateAsync(user,id,bool); dev only exposes MarkReadAsync and cannot unset read state. Covered separately without claiming an equivalent API.")]
    [Trait("Category", "NOT-02")]
    public Task SetReadStateAsync_UTC01() => Task.CompletedTask;
    [Fact(Skip = "SetReadStateAsync is absent from dev; missing-notification behavior of MarkReadAsync is covered separately.")]
    [Trait("Category", "NOT-02")]
    public Task SetReadStateAsync_UTC02() => Task.CompletedTask;
    [Fact(Skip = "SetReadStateAsync is absent from dev; Excel UTC03 asks to set read=false, unsupported by MarkReadAsync.")]
    [Trait("Category", "NOT-02")]
    public Task SetReadStateAsync_UTC03() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "NOT-03")]
    public async Task MarkAllReadAsync_UTC01()
    {
        await using var c = new Context(); c.Seed(); c.Seed(); var read = c.Seed(); read.IsRead = true; read.ReadAt = Now.AddDays(-1); var other = c.Seed(Guid.NewGuid()); var push = c.Seed(); push.Channel = NotificationChannel.Push; await c.Db.SaveChangesAsync();
        Assert.Equal(2, (await c.Service.MarkAllReadAsync(c.UserId, default)).UpdatedCount); c.Db.ChangeTracker.Clear();
        var all = await c.Db.Notifications.ToListAsync(); Assert.False(all.Single(n => n.Id == other.Id).IsRead); Assert.False(all.Single(n => n.Id == push.Id).IsRead); Assert.Equal(Now.AddDays(-1), all.Single(n => n.Id == read.Id).ReadAt);
        Assert.Equal(2, all.Count(n => n.ReadAt == Now)); Assert.Equal(0, (await c.Service.MarkAllReadAsync(c.UserId, default)).UpdatedCount);
    }
    [Fact]
    [Trait("Category", "NOT-03")]
    public async Task MarkAllReadAsync_UTC02()
    { await using var c = new Context(); c.Seed(); await c.Db.SaveChangesAsync(); Assert.Equal(0, (await c.Service.MarkAllReadAsync(Guid.NewGuid(), default)).UpdatedCount); Assert.False((await c.Db.Notifications.SingleAsync()).IsRead); }
    [Fact]
    [Trait("Category", "NOT-03")]
    public async Task MarkAllReadAsync_UTC03()
    { await using var c = new Context(); Assert.Equal(0, (await c.Service.MarkAllReadAsync(Guid.Empty, default)).UpdatedCount); Assert.Empty(c.Db.Notifications); }

    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task MarkReadAsync_PersistsReadTimeAndIsIdempotent()
    {
        await using var c = new Context(); var item = c.Seed(); await c.Db.SaveChangesAsync(); var result = await c.Service.MarkReadAsync(c.UserId, item.Id, default); Assert.True(result.IsRead); Assert.Equal(Now, result.ReadAt); c.Db.ChangeTracker.Clear(); Assert.True((await c.Db.Notifications.SingleAsync()).IsRead);
        Assert.Equal(Now, (await c.Service.MarkReadAsync(c.UserId, item.Id, default)).ReadAt);
    }
    [Fact]
    [Trait("Category", "NOT-02")]
    public async Task MarkReadAsync_DeniesOtherUserAndPushChannel()
    {
        await using var c = new Context(); var own = c.Seed(); var push = c.Seed(); push.Channel = NotificationChannel.Push; await c.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.MarkReadAsync(Guid.NewGuid(), own.Id, default)); Assert.Equal("Notification not found.", ex.Message);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.MarkReadAsync(c.UserId, push.Id, default)); c.Db.ChangeTracker.Clear(); Assert.All(c.Db.Notifications, n => Assert.False(n.IsRead));
    }
    [Fact]
    [Trait("Category", "NOT-01")]
    public async Task GetForUserAsync_IncludesReadWhenRequestedAndRejectsZeroLimit()
    {
        await using var c = new Context(); var item = c.Seed(); item.IsRead = true; await c.Db.SaveChangesAsync(); Assert.True(Assert.Single(await c.Service.GetForUserAsync(c.UserId, false, 100, default)).IsRead); await Assert.ThrowsAsync<ArgumentException>(() => c.Service.GetForUserAsync(c.UserId, false, 0, default));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid UserId { get; } = Guid.NewGuid();
        public NotificationService Service => new(Db, new Clock());
        public Notification Seed(Guid? user = null) { var item = new Notification { Id = Guid.NewGuid(), UserId = user ?? UserId, Type = "ParcelStored", Title = "Stored", Message = "Parcel in locker", Channel = NotificationChannel.InApp, CreatedAt = Now }; Db.Add(item); return item; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
