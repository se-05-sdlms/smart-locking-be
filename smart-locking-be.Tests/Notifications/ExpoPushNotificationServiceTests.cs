using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Notifications;

public sealed class ExpoPushNotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC01()
    {
        await using var c = new Context(); var notification = c.Notification(); c.Device(notification.UserId); await c.Db.SaveChangesAsync();
        await c.Service.TrySendAsync(notification.Id); c.Db.ChangeTracker.Clear();
        var persisted = await c.Db.Notifications.SingleAsync(); Assert.Equal(NotificationDeliveryStatus.Sent, persisted.DeliveryStatus); Assert.Equal(Now, persisted.SentAt);
        Assert.Equal("https://exp.host/--/api/v2/push/send", c.Handler.Url); Assert.Equal(HttpMethod.Post, c.Handler.Method);
        using var payload = JsonDocument.Parse(c.Handler.Body!); var item = Assert.Single(payload.RootElement.EnumerateArray());
        Assert.Equal("ExpoPushToken[test]", item.GetProperty("to").GetString()); Assert.Equal("Approval", item.GetProperty("title").GetString()); Assert.Equal("Message", item.GetProperty("body").GetString());
        await c.Service.TrySendAsync(notification.Id); Assert.Equal(1, c.Handler.Count);
    }
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC02()
    {
        // Excel permits missing/empty. TrySendAsync intentionally catches missing-record errors.
        await using var c = new Context(); await c.Service.TrySendAsync(Guid.NewGuid()); Assert.Equal(0, c.Handler.Count); Assert.Empty(c.Db.Notifications);
    }
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC03()
    { await using var c = new Context(); await c.Service.TrySendAsync(Guid.Empty); Assert.Equal(0, c.Handler.Count); Assert.Empty(c.Db.Notifications); }
    [Fact]
    [Trait("Category", "PSH-02")]
    public async Task RetryPendingDeliveryApprovalNotificationsAsync_UTC01()
    {
        await using var c = new Context(); var notification = c.Notification(); notification.DeliveryRequest = c.Request(DeliveryRequestStatus.PendingApproval, Now.AddMinutes(1)); c.Device(notification.UserId); await c.Db.SaveChangesAsync();
        Assert.Equal(1, await c.Service.RetryPendingDeliveryApprovalNotificationsAsync()); c.Db.ChangeTracker.Clear();
        Assert.Equal(NotificationDeliveryStatus.Sent, (await c.Db.Notifications.SingleAsync()).DeliveryStatus); Assert.Equal(1, c.Handler.Count);
        Assert.Equal(0, await c.Service.RetryPendingDeliveryApprovalNotificationsAsync());
    }
    [Fact]
    [Trait("Category", "PSH-02")]
    public async Task RetryPendingDeliveryApprovalNotificationsAsync_UTC02()
    { await using var c = new Context(); Assert.Equal(0, await c.Service.RetryPendingDeliveryApprovalNotificationsAsync()); Assert.Equal(0, c.Handler.Count); }
    [Fact]
    [Trait("Category", "PSH-02")]
    public async Task RetryPendingDeliveryApprovalNotificationsAsync_UTC03()
    {
        await using var c = new Context(); var expired = c.Notification(); expired.DeliveryRequest = c.Request(DeliveryRequestStatus.PendingApproval, Now);
        var deposited = c.Notification(); deposited.DeliveryRequest = c.Request(DeliveryRequestStatus.Deposited, Now.AddMinutes(1));
        var sent = c.Notification(); sent.DeliveryStatus = NotificationDeliveryStatus.Sent; sent.DeliveryRequest = c.Request(DeliveryRequestStatus.PendingApproval, Now.AddMinutes(1));
        var inApp = c.Notification(); inApp.Channel = NotificationChannel.InApp; inApp.DeliveryRequest = c.Request(DeliveryRequestStatus.PendingApproval, Now.AddMinutes(1)); await c.Db.SaveChangesAsync();
        Assert.Equal(0, await c.Service.RetryPendingDeliveryApprovalNotificationsAsync()); Assert.Equal(0, c.Handler.Count); Assert.Equal(4, await c.Db.Notifications.CountAsync());
    }
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_DeactivatesUnregisteredDevice()
    {
        await using var c = new Context(); c.Handler.Json = "{\"data\":[{\"status\":\"error\",\"details\":{\"error\":\"DeviceNotRegistered\"}}]}"; var notification = c.Notification(); c.Device(notification.UserId); await c.Db.SaveChangesAsync();
        await c.Service.TrySendAsync(notification.Id); c.Db.ChangeTracker.Clear(); Assert.False((await c.Db.DeviceInstallations.SingleAsync()).IsActive); var persisted = await c.Db.Notifications.SingleAsync(); Assert.Equal(NotificationDeliveryStatus.Failed, persisted.DeliveryStatus); Assert.Null(persisted.SentAt);
    }
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_PersistsFailureOnHttpError()
    {
        await using var c = new Context(); c.Handler.Status = HttpStatusCode.ServiceUnavailable; var notification = c.Notification(); c.Device(notification.UserId); await c.Db.SaveChangesAsync();
        await c.Service.TrySendAsync(notification.Id); c.Db.ChangeTracker.Clear(); Assert.Equal(NotificationDeliveryStatus.Failed, (await c.Db.Notifications.SingleAsync()).DeliveryStatus); Assert.True((await c.Db.DeviceInstallations.SingleAsync()).IsActive);
    }
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_FailsWithoutActiveDevicesWithoutCallingHttp()
    {
        await using var c = new Context(); var notification = c.Notification(); var device = c.Device(notification.UserId); device.IsActive = false; await c.Db.SaveChangesAsync();
        await c.Service.TrySendAsync(notification.Id); c.Db.ChangeTracker.Clear(); Assert.Equal(NotificationDeliveryStatus.Failed, (await c.Db.Notifications.SingleAsync()).DeliveryStatus); Assert.Equal(0, c.Handler.Count);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Body { get; private set; }
        public string? Url { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string Json { get; set; } = "{\"data\":[{\"status\":\"ok\",\"id\":\"ticket\"}]}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++; Url = request.RequestUri!.ToString(); Method = request.Method; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(Json, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Handler Handler { get; } = new();
        private readonly HttpClient client;
        public ExpoPushNotificationService Service => new(Db, client, new Clock(), NullLogger<ExpoPushNotificationService>.Instance);
        public Context() { client = new HttpClient(Handler); }
        public Notification Notification()
        { var item = new Notification { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Type = "DeliveryApprovalRequested", Title = "Approval", Message = "Message", Channel = NotificationChannel.Push, DeliveryStatus = NotificationDeliveryStatus.Pending, CreatedAt = Now }; Db.Add(item); return item; }
        public DeviceInstallation Device(Guid userId)
        { var item = new DeviceInstallation { Id = Guid.NewGuid(), UserId = userId, InstallationId = Guid.NewGuid().ToString(), ExpoPushToken = "ExpoPushToken[test]", Platform = "Android", IsActive = true, CreatedAt = Now }; Db.Add(item); return item; }
        public DeliveryRequest Request(DeliveryRequestStatus status, DateTimeOffset deadline) { var item = new DeliveryRequest { Id = Guid.NewGuid(), Locker = new Locker { Id = Guid.NewGuid(), Code = Guid.NewGuid().ToString(), Address = "Address", DeviceIdentifier = Guid.NewGuid().ToString() }, SystemPolicy = new SystemPolicy { Id = Guid.NewGuid(), Currency = "VND" }, GuestSessionTokenHash = "hash", Status = status, ApprovalExpiresAt = deadline }; Db.Add(item); return item; }
        public async ValueTask DisposeAsync() { client.Dispose(); await Db.DisposeAsync(); }
    }
}
