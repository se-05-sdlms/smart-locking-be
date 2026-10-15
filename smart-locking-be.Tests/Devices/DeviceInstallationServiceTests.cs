using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.DeviceInstallations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Devices;

public sealed class DeviceInstallationServiceTests
{
    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC01()
    {
        await using var c = new Context(); var before = DateTimeOffset.UtcNow;
        var result = await c.Service.RegisterAsync(c.User.Id, new(" device-1 ", " ExpoPushToken[token-1] ", " android "));
        c.Db.ChangeTracker.Clear(); var persisted = await c.Db.DeviceInstallations.SingleAsync();
        Assert.Equal(result.Id, persisted.Id); Assert.Equal(c.User.Id, persisted.UserId);
        Assert.Equal("device-1", result.InstallationId); Assert.Equal("ExpoPushToken[token-1]", result.ExpoPushToken); Assert.Equal("Android", result.Platform); Assert.True(result.IsActive);
        Assert.Equal(result.UpdatedAt, persisted.UpdatedAt); Assert.InRange(persisted.CreatedAt, before, DateTimeOffset.UtcNow);
    }
    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC02()
    {
        await using var c = new Context(); c.Device("device-1", "ExpoPushToken[first]"); c.Device("device-2", "ExpoPushToken[second]"); await c.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service.RegisterAsync(c.User.Id, new("device-1", "ExpoPushToken[second]", "ios")));
        Assert.Equal("Expo push token đã được đăng ký cho thiết bị khác.", ex.Message);
        c.Db.ChangeTracker.Clear(); Assert.Equal(2, await c.Db.DeviceInstallations.CountAsync());
        Assert.Equal("ExpoPushToken[first]", (await c.Db.DeviceInstallations.SingleAsync(d => d.InstallationId == "device-1")).ExpoPushToken);
        Assert.All(c.Db.DeviceInstallations, d => Assert.True(d.IsActive));
    }
    [Fact(Skip = "Excel DEV-01 UTC03 uses empty user/null request but gives no concrete expected exception; dev rejects inactive/missing resident first.")]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UTC03()
    { await using var c = new Context(); Assert.Null(await c.Service.RegisterAsync(Guid.Empty, null!)); }
    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC01()
    {
        await using var c = new Context(); var device = c.Device("device-1", "ExpoPushToken[first]"); await c.Db.SaveChangesAsync();
        await c.Service.DeactivateAsync(c.User.Id, " device-1 "); c.Db.ChangeTracker.Clear();
        var persisted = await c.Db.DeviceInstallations.SingleAsync(); Assert.Equal(device.Id, persisted.Id); Assert.False(persisted.IsActive); Assert.Equal("ExpoPushToken[first]", persisted.ExpoPushToken);
        await c.Service.DeactivateAsync(c.User.Id, "device-1"); Assert.Single(c.Db.DeviceInstallations);
    }
    [Fact]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC02()
    {
        await using var c = new Context(); c.Device("device-1", "ExpoPushToken[first]"); await c.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Service.DeactivateAsync(Guid.NewGuid(), "device-1"));
        Assert.Equal("Không tìm thấy thiết bị đã đăng ký.", ex.Message); c.Db.ChangeTracker.Clear(); Assert.True((await c.Db.DeviceInstallations.SingleAsync()).IsActive);
    }
    [Fact(Skip = "Excel DEV-02 UTC03 uses Guid.Empty for a string installationId and leaves expected exception undefined.")]
    [Trait("Category", "DEV-02")]
    public async Task DeactivateAsync_UTC03()
    { await using var c = new Context(); await c.Service.DeactivateAsync(Guid.Empty, ""); }

    [Fact]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_UpdatesExistingInstallationWithoutDuplicating()
    {
        await using var c = new Context(); var device = c.Device("device-1", "ExpoPushToken[old]"); device.IsActive = false; await c.Db.SaveChangesAsync();
        var result = await c.Service.RegisterAsync(c.User.Id, new("device-1", "ExponentPushToken[new]", "IOS")); c.Db.ChangeTracker.Clear();
        var persisted = await c.Db.DeviceInstallations.SingleAsync(); Assert.Equal(device.Id, result.Id); Assert.True(persisted.IsActive); Assert.Equal("iOS", persisted.Platform); Assert.Equal("ExponentPushToken[new]", persisted.ExpoPushToken);
    }
    [Theory]
    [InlineData(UserRole.Resident, UserStatus.Disabled)]
    [InlineData(UserRole.Administrator, UserStatus.Active)]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_RejectsUnauthorizedAccount(UserRole role, UserStatus status)
    {
        await using var c = new Context(); c.User.Role = role; c.User.Status = status; await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Service.RegisterAsync(c.User.Id, new("device", "ExpoPushToken[token]", "android"))); Assert.Empty(c.Db.DeviceInstallations);
    }
    [Theory]
    [InlineData("", "ExpoPushToken[token]", "android", "Giá trị không được để trống.")]
    [InlineData("device", "invalid", "android", "ExpoPushToken không hợp lệ.")]
    [InlineData("device", "ExpoPushToken[token]", "windows", "Platform chỉ hỗ trợ Android hoặc iOS.")]
    [Trait("Category", "DEV-01")]
    public async Task RegisterAsync_RejectsInvalidInputWithoutSaving(string installation, string token, string platform, string message)
    {
        await using var c = new Context(); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.RegisterAsync(c.User.Id, new(installation, token, platform))); Assert.StartsWith(message, ex.Message); Assert.Empty(c.Db.DeviceInstallations);
    }
    private sealed class Context : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public User User { get; } = new() { Id = Guid.NewGuid(), Role = UserRole.Resident, Status = UserStatus.Active, PhoneNumber = "0912345678", PasswordHash = "hash" };
        public DeviceInstallationService Service => new(Db);
        public Context() { Db.Add(User); Db.SaveChanges(); }
        public DeviceInstallation Device(string installation, string token)
        {
            var device = new DeviceInstallation { Id = Guid.NewGuid(), UserId = User.Id, InstallationId = installation, ExpoPushToken = token, Platform = "Android", IsActive = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) }; Db.Add(device); return device;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
