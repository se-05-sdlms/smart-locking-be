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

public sealed class DeviceInstallationRegressionTests
{
    [Fact]
    [Trait("Category", "DEV-01")]
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
