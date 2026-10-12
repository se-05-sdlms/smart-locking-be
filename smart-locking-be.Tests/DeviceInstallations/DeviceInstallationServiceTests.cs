using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.DeviceInstallations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.DeviceInstallations;

public sealed class DeviceInstallationServiceTests
{
    #region DEV-01: RegisterAsync
    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        DeviceInstallationResponse response = await service.RegisterAsync(
            resident.Id,
            "phone-1",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-1]", "android"));

        Assert.Equal("phone-1", response.InstallationId);
        Assert.Equal("ExpoPushToken[token-1]", response.ExpoPushToken);
        Assert.Equal("Android", response.Platform);
        Assert.True(response.IsActive);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        User resident2 = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        await service.RegisterAsync(
            resident.Id,
            "phone-1",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-1]", "android"));
        await service.RegisterAsync(
            resident2.Id,
            "phone-2",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-2]", "android"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            resident2.Id,
            "phone-2",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-1]", "android")));

        Assert.Equal("Expo push token đã được đăng ký cho thiết bị khác.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new DeviceInstallationService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() => service.RegisterAsync(
            Guid.Empty, "", null!, default));
    }
    #endregion

    #region DEV-02: DeactivateAsync
    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);
        await service.RegisterAsync(
            resident.Id,
            "phone-1",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-1]", "Android"));

        await service.DeactivateAsync(resident.Id, "phone-1");

        Assert.False((await dbContext.DeviceInstallations.SingleAsync()).IsActive);
    }

    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeactivateAsync(
            resident.Id, "non-existing-phone"));

        Assert.Equal("Không tìm thấy thiết bị đã đăng ký.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new DeviceInstallationService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() => service.DeactivateAsync(
            Guid.Empty, ""));
    }
    #endregion

    #region PSH-01: TrySendAsync
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        Guid pushNotificationId = service.EnqueueDeliveryApprovalRequest(
            resident.Id,
            Guid.NewGuid(),
            "LOCKER-01");
        await dbContext.SaveChangesAsync();

        await service.TrySendAsync(pushNotificationId);

        List<Notification> notifications = await dbContext.Notifications.ToListAsync();
        Assert.Collection(
            notifications.OrderBy(notification => notification.Channel),
            notification =>
            {
                Assert.Equal(NotificationChannel.InApp, notification.Channel);
                Assert.Equal(NotificationDeliveryStatus.Sent, notification.DeliveryStatus);
            },
            notification =>
            {
                Assert.Equal(NotificationChannel.Push, notification.Channel);
                Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
            });
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        // Non-existing notification ID does not throw unhandled exception (handled internally in TrySendAsync)
        await service.TrySendAsync(Guid.NewGuid());
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        await service.TrySendAsync(Guid.Empty);
    }
    #endregion

    #region PSH-02: RetryPendingDeliveryApprovalNotificationsAsync
    [Fact]
    [Trait("Category", "PSH-02")]
    public async Task RetryPendingDeliveryApprovalNotificationsAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeliveryRequest deliveryRequest = new()
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = null,
            LockerId = Guid.NewGuid(),
            SystemPolicyId = Guid.NewGuid(),
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.PendingApproval,
            LastActivityAt = now,
            SessionExpiresAt = now.AddMinutes(15),
            ApprovalExpiresAt = now.AddMinutes(10),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.DeliveryRequests.Add(deliveryRequest);

        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);
        Guid pushNotificationId = service.EnqueueDeliveryApprovalRequest(
            resident.Id,
            deliveryRequest.Id,
            "LOCKER-01");
        await dbContext.SaveChangesAsync();
        await service.TrySendAsync(pushNotificationId);

        dbContext.DeviceInstallations.Add(new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            InstallationId = "phone-1",
            ExpoPushToken = "ExpoPushToken[token-1]",
            Platform = "Android",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        int retried = await service.RetryPendingDeliveryApprovalNotificationsAsync();

        Notification notification = await dbContext.Notifications.SingleAsync(item => item.Id == pushNotificationId);
        Assert.Equal(1, retried);
        Assert.Equal(NotificationDeliveryStatus.Sent, notification.DeliveryStatus);
        Assert.NotNull(notification.SentAt);
    }

    [Fact]
    [Trait("Category", "PSH-02")]
    public async Task RetryPendingDeliveryApprovalNotificationsAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        int retried = await service.RetryPendingDeliveryApprovalNotificationsAsync();
        Assert.Equal(0, retried);
    }

    [Fact]
    [Trait("Category", "PSH-02")]
    public async Task RetryPendingDeliveryApprovalNotificationsAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        int retried = await service.RetryPendingDeliveryApprovalNotificationsAsync(default);
        Assert.Equal(0, retried);
    }
    #endregion

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<User> SeedResidentAsync(ApplicationDbContext dbContext)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0901234567",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private sealed class ExpoSuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"status\":\"ok\",\"id\":\"ticket-1\"}]}",
                    Encoding.UTF8,
                    "application/json")
            });
    }
}
