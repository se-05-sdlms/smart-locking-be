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
    [Fact]
    public async Task RegisterAsync_AllowsMultipleDevicesForResident()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        await service.RegisterAsync(
            resident.Id,
            new RegisterDeviceInstallationRequest("phone-1", "ExpoPushToken[token-1]", "android"));
        await service.RegisterAsync(
            resident.Id,
            new RegisterDeviceInstallationRequest("phone-2", "ExpoPushToken[token-2]", "Android"));

        Assert.Equal(2, await dbContext.DeviceInstallations.CountAsync());
        Assert.All(dbContext.DeviceInstallations, device => Assert.True(device.IsActive));
    }

    [Fact]
    public async Task RegisterAsync_SameInstallationUpdatesTokenWithoutDuplicate()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);

        await service.RegisterAsync(
            resident.Id,
            new RegisterDeviceInstallationRequest("phone-1", "ExpoPushToken[old-token]", "Android"));
        await service.RegisterAsync(
            resident.Id,
            new RegisterDeviceInstallationRequest("phone-1", "ExpoPushToken[new-token]", "Android"));

        DeviceInstallation installation = await dbContext.DeviceInstallations.SingleAsync();
        Assert.Equal("ExpoPushToken[new-token]", installation.ExpoPushToken);
    }

    [Fact]
    public async Task DeactivateAsync_OwnDeviceMarksItInactive()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new DeviceInstallationService(dbContext);
        await service.RegisterAsync(
            resident.Id,
            new RegisterDeviceInstallationRequest("phone-1", "ExpoPushToken[token-1]", "Android"));

        await service.DeactivateAsync(resident.Id, "phone-1");

        Assert.False((await dbContext.DeviceInstallations.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task TrySendAsync_WithoutDeviceKeepsInAppAndMarksPushAsFailed()
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
    public async Task RetryPendingDeliveryApprovalNotificationsAsync_WhenDeviceRegisters_SendsFailedPush()
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
