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
    public async Task SendDeliveryApprovalRequestAsync_WithoutDeviceStoresFailedNotification()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = await SeedResidentAsync(dbContext);
        var service = new ExpoPushNotificationService(
            dbContext,
            NullLogger<ExpoPushNotificationService>.Instance);

        await service.SendDeliveryApprovalRequestAsync(
            resident.Id,
            Guid.NewGuid(),
            "LOCKER-01");

        Notification notification = await dbContext.Notifications.SingleAsync();
        Assert.Equal(NotificationChannel.Push, notification.Channel);
        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
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
}
