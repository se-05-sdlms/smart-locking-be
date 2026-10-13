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
            "device-001",
            new RegisterDeviceInstallationRequest("ExpoPushToken[valid-token]", "android"));

        Assert.Equal("device-001", response.InstallationId);
        Assert.Equal("ExpoPushToken[valid-token]", response.ExpoPushToken);
        Assert.Equal("Android", response.Platform);
        Assert.True(response.IsActive);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RegisterAsync(
            Guid.NewGuid(),
            "device-001",
            new RegisterDeviceInstallationRequest("ExpoPushToken[valid-token]", "android")));

        Assert.Equal("Tài khoản Cư dân không hợp lệ hoặc không còn hoạt động.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            resident.Id,
            "",
            new RegisterDeviceInstallationRequest("ExpoPushToken[valid-token]", "android")));

        Assert.Contains("Giá trị không được để trống.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            resident.Id,
            new string('a', 101),
            new RegisterDeviceInstallationRequest("ExpoPushToken[valid-token]", "android")));

        Assert.Contains("Giá trị không được vượt quá 100 ký tự.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            resident.Id,
            "device-001",
            new RegisterDeviceInstallationRequest("invalid-token", "android")));

        Assert.Contains("ExpoPushToken không hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(
            resident.Id,
            "device-001",
            new RegisterDeviceInstallationRequest("ExpoPushToken[valid-token]", "windows")));

        Assert.Contains("Platform chỉ hỗ trợ Android hoặc iOS.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident1 = await SeedResidentAsync(dbContext);
        User resident2 = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        await service.RegisterAsync(
            resident1.Id,
            "device-001",
            new RegisterDeviceInstallationRequest("ExpoPushToken[valid-token]", "android"));
        await service.RegisterAsync(
            resident2.Id,
            "device-002",
            new RegisterDeviceInstallationRequest("ExpoPushToken[other-token]", "android"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            resident2.Id,
            "device-002",
            new RegisterDeviceInstallationRequest("ExpoPushToken[valid-token]", "android")));

        Assert.Equal("Expo push token đã được đăng ký cho thiết bị khác.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        await service.RegisterAsync(
            resident.Id,
            "device-001",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-01]", "android"));

        DeviceInstallationResponse updated = await service.RegisterAsync(
            resident.Id,
            "device-001",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-01]", "IOS"));

        Assert.Equal("iOS", updated.Platform);
        Assert.True(updated.IsActive);
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
            "device-001",
            new RegisterDeviceInstallationRequest("ExpoPushToken[token-1]", "Android"));

        await service.DeactivateAsync(resident.Id, "device-001");

        Assert.False((await dbContext.DeviceInstallations.SingleAsync()).IsActive);
    }

    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.DeactivateAsync(
            resident.Id, ""));

        Assert.Contains("Giá trị không được để trống.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.DeactivateAsync(
            resident.Id, new string('a', 101)));

        Assert.Contains("Giá trị không được vượt quá 100 ký tự.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeactivateAsync(
            resident.Id, "non-existing-device"));

        Assert.Equal("Không tìm thấy thiết bị đã đăng ký.", ex.Message);
    }
    #endregion

    #region PSH-01: TrySendAsync
    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        dbContext.DeviceInstallations.Add(new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            InstallationId = "device-001",
            ExpoPushToken = "ExpoPushToken[token-1]",
            Platform = "Android",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

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

        Notification pushNotification = await dbContext.Notifications.SingleAsync(n => n.Id == pushNotificationId);
        Assert.Equal(NotificationDeliveryStatus.Sent, pushNotification.DeliveryStatus);
        Assert.NotNull(pushNotification.SentAt);
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

        // Non-existing notification ID does not throw unhandled exception
        await service.TrySendAsync(Guid.NewGuid());
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var notif = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            Channel = NotificationChannel.InApp,
            DeliveryStatus = NotificationDeliveryStatus.Sent,
            Title = "Test",
            Message = "Test message",
            Type = "TestType",
            CreatedAt = now,
            SentAt = now
        };
        dbContext.Notifications.Add(notif);
        await dbContext.SaveChangesAsync();

        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        await service.TrySendAsync(notif.Id);

        Assert.Equal(NotificationDeliveryStatus.Sent, notif.DeliveryStatus);
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoSuccessHandler()),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        // Push notification for resident with no active device installation
        Guid pushNotificationId = service.EnqueueDeliveryApprovalRequest(
            resident.Id,
            Guid.NewGuid(),
            "LOCKER-01");
        await dbContext.SaveChangesAsync();

        await service.TrySendAsync(pushNotificationId);

        Notification pushNotification = await dbContext.Notifications.SingleAsync(n => n.Id == pushNotificationId);
        Assert.Equal(NotificationDeliveryStatus.Failed, pushNotification.DeliveryStatus);
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        dbContext.DeviceInstallations.Add(new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            InstallationId = "device-001",
            ExpoPushToken = "ExpoPushToken[token-1]",
            Platform = "Android",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoErrorHandler(HttpStatusCode.InternalServerError, "Server Error")),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        Guid pushNotificationId = service.EnqueueDeliveryApprovalRequest(
            resident.Id,
            Guid.NewGuid(),
            "LOCKER-01");
        await dbContext.SaveChangesAsync();

        await service.TrySendAsync(pushNotificationId);

        Notification pushNotification = await dbContext.Notifications.SingleAsync(n => n.Id == pushNotificationId);
        Assert.Equal(NotificationDeliveryStatus.Failed, pushNotification.DeliveryStatus);
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        dbContext.DeviceInstallations.Add(new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            InstallationId = "device-001",
            ExpoPushToken = "ExpoPushToken[token-1]",
            Platform = "Android",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();

        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoErrorHandler(HttpStatusCode.OK, "{\"data\":[{\"status\":\"error\",\"message\":\"Failure\"}]}")),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        Guid pushNotificationId = service.EnqueueDeliveryApprovalRequest(
            resident.Id,
            Guid.NewGuid(),
            "LOCKER-01");
        await dbContext.SaveChangesAsync();

        await service.TrySendAsync(pushNotificationId);

        Notification pushNotification = await dbContext.Notifications.SingleAsync(n => n.Id == pushNotificationId);
        Assert.Equal(NotificationDeliveryStatus.Failed, pushNotification.DeliveryStatus);
        Assert.Null(pushNotification.SentAt);
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var device = new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            InstallationId = "device-001",
            ExpoPushToken = "ExpoPushToken[token-1]",
            Platform = "Android",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.DeviceInstallations.Add(device);
        await dbContext.SaveChangesAsync();

        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoErrorHandler(HttpStatusCode.OK, "{\"data\":[{\"status\":\"error\",\"details\":{\"error\":\"DeviceNotRegistered\"}}]} ")),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        Guid pushNotificationId = service.EnqueueDeliveryApprovalRequest(
            resident.Id,
            Guid.NewGuid(),
            "LOCKER-01");
        await dbContext.SaveChangesAsync();

        await service.TrySendAsync(pushNotificationId);

        Assert.False(device.IsActive);
    }

    [Fact]
    [Trait("Category", "PSH-01")]
    public async Task TrySendAsync_UTC08()
    {
        // When send fails and MarkFailedAsync also fails, exception does not escape TrySendAsync
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new ExpoPushNotificationService(
            dbContext,
            new HttpClient(new ExpoErrorHandler(HttpStatusCode.InternalServerError, "Error")),
            TimeProvider.System,
            NullLogger<ExpoPushNotificationService>.Instance);

        // TrySendAsync on empty/unmanaged context should gracefully handle any secondary failure
        await service.TrySendAsync(Guid.NewGuid());
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
            PhoneNumber = $"090{Guid.NewGuid():N}"[..10],
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

    private sealed class ExpoErrorHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json")
            });
    }
}
