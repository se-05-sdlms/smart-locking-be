using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Lockers;

public sealed class LockerAccessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    #region LAC-01: LockerAccessService.OpenAsync
    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(dbContext, dispatcher, new FixedTimeProvider(Now));

        OpenLockerResponse response = await service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            LockerAccessType.ResidentPickup,
            LockerAccessMethod.RemoteApp,
            "127.0.0.1",
            "resident-mobile"));

        Assert.Equal(LockerAccessResult.Succeeded, response.Result);
        Assert.Single(dispatcher.Commands);
        Assert.Single(dbContext.LockerAccessEvents);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            null,
            null, // DeliveryRequestId is null
            null,
            null,
            LockerAccessType.ShipperDropOff,
            LockerAccessMethod.GuestSession,
            null, null)));

        Assert.Equal("Thiếu định danh nghiệp vụ cho thao tác mở locker.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            null, // UserId is null
            null,
            Guid.NewGuid(),
            null,
            LockerAccessType.ResidentPickup,
            LockerAccessMethod.RemoteApp,
            null, null)));

        Assert.Equal("Mở locker từ ứng dụng yêu cầu người dùng đã xác thực.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);
        string longIp = new string('1', 46);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            LockerAccessType.ResidentPickup,
            LockerAccessMethod.RemoteApp,
            longIp, null)));

        Assert.Equal("Địa chỉ IP không hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            Guid.NewGuid(), // non-existent compartment
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null)));

        Assert.Equal("Không tìm thấy ngăn locker.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAsync(dbContext);
        (Locker locker2, LockerCompartment comp2) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            comp2.Id, // belongs to locker2
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null)));

        Assert.Equal("Ngăn locker không thuộc locker được yêu cầu.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        locker.OperationalStatus = LockerOperationalStatus.OutOfService;
        await dbContext.SaveChangesAsync();

        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);
        OpenLockerResponse response = await service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null));

        Assert.Equal(LockerAccessResult.Blocked, response.Result);
        Assert.Equal("Locker hiện không sẵn sàng vận hành.", response.FailureReason);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext, LockerConnectionStatus.Offline);

        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);
        OpenLockerResponse response = await service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null));

        Assert.Equal(LockerAccessResult.Blocked, response.Result);
        Assert.Equal("Locker hiện không trực tuyến.", response.FailureReason);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC09()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        compartment.OperationalStatus = LockerCompartmentOperationalStatus.OutOfService;
        await dbContext.SaveChangesAsync();

        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);
        OpenLockerResponse response = await service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null));

        Assert.Equal(LockerAccessResult.Blocked, response.Result);
        Assert.Equal("Ngăn locker hiện không sẵn sàng vận hành.", response.FailureReason);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC10()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);

        var service = new LockerAccessService(dbContext, new FailingDispatcher(), TimeProvider.System);
        OpenLockerResponse response = await service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null));

        Assert.Equal(LockerAccessResult.Failed, response.Result);
        Assert.Equal("Không thể gửi lệnh mở ngăn tới locker.", response.FailureReason);
    }
    #endregion

    #region LCM-01: LockerCommandDispatcher.DispatchUnlockAsync
    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC01()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "true"
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        await dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "locker-001", 1));
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC02()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "false",
                ["Mqtt:Host"] = ""
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "locker-001", 1)));

        Assert.Equal("MQTT broker chưa được cấu hình.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC03()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "true",
                ["Mqtt:Host"] = "broker.local",
                ["Mqtt:Port"] = "1883",
                ["Mqtt:UseTls"] = "false"
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        await dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "locker-001", 1));
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC04()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "true",
                ["Mqtt:Host"] = "broker.local",
                ["Mqtt:Port"] = "invalid"
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        await dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "locker-001", 1));
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC05()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "true",
                ["Mqtt:Host"] = "broker.local"
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        await dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "locker-001", 1));
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC06()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "true",
                ["Mqtt:Host"] = "broker.local",
                ["Mqtt:Username"] = "admin",
                ["Mqtt:Password"] = "pass"
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        await dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "locker-001", 1));
    }
    #endregion

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(Locker Locker, LockerCompartment Compartment)> SeedLockerAsync(
        ApplicationDbContext dbContext,
        LockerConnectionStatus connectionStatus = LockerConnectionStatus.Online)
    {
        Locker locker = new()
        {
            Id = Guid.NewGuid(),
            Code = "L-01",
            Address = "Tòa A",
            RecoveryAddress = "Quầy lễ tân",
            DeviceIdentifier = "esp32-l01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = connectionStatus,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        LockerCompartment compartment = new()
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "A01",
            HardwareCode = "relay-01",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        dbContext.AddRange(locker, compartment);
        await dbContext.SaveChangesAsync();
        return (locker, compartment);
    }

    private sealed class RecordingDispatcher : ILockerCommandDispatcher
    {
        public List<LockerUnlockCommand> Commands { get; } = [];

        public Task DispatchUnlockAsync(
            LockerUnlockCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingDispatcher : ILockerCommandDispatcher
    {
        public Task DispatchUnlockAsync(
            LockerUnlockCommand command,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Broker unavailable");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
