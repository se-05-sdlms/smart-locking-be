using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class LockerAccessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    #region LAC-01: OpenAsync
    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(dbContext, dispatcher, new FixedTimeProvider(Now));
        Guid userId = Guid.NewGuid();
        Guid parcelId = Guid.NewGuid();

        OpenLockerResponse response = await service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment.Id,
            userId,
            null,
            parcelId,
            null,
            LockerAccessType.ResidentPickup,
            LockerAccessMethod.RemoteApp,
            " 127.0.0.1 ",
            " resident-mobile "));

        LockerAccessEvent accessEvent = await dbContext.LockerAccessEvents.SingleAsync();
        Assert.Equal(LockerAccessResult.Succeeded, response.Result);
        Assert.Equal(Now, response.OccurredAt);
        Assert.Equal("127.0.0.1", accessEvent.IpAddress);
        Assert.Equal("resident-mobile", accessEvent.DeviceContext);
        Assert.Equal(
            new LockerUnlockCommand(accessEvent.Id, locker.DeviceIdentifier, compartment.HardwareChannel),
            Assert.Single(dispatcher.Commands));
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            Guid.NewGuid(), // non-existing compartment
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null)));

        Assert.Equal("Không tìm thấy ngăn locker.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, _) = await SeedLockerAsync(dbContext);
        (Locker locker2, LockerCompartment compartment2) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync(new OpenLockerRequest(
            locker.Id,
            compartment2.Id, // belongs to locker2
            null, null, null, null,
            LockerAccessType.Maintenance,
            LockerAccessMethod.SystemAuthorization,
            null, null)));

        Assert.Equal("Ngăn locker không thuộc locker được yêu cầu.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LAC-01")]
    public async Task OpenAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new LockerAccessService(dbContext, new RecordingDispatcher(), TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.OpenAsync(null!));
    }
    #endregion

    #region LCM-01: DispatchUnlockAsync
    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC01()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "true"
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        await dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "dev-1", 1));
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC02()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Simulate"] = "false",
                ["Mqtt:Host"] = ""
            })
            .Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchUnlockAsync(new LockerUnlockCommand(Guid.NewGuid(), "dev-1", 1)));

        Assert.Equal("MQTT broker chưa được cấu hình.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LCM-01")]
    public async Task DispatchUnlockAsync_UTC03()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dispatcher = new MqttLockerCommandDispatcher(config);

        await Assert.ThrowsAnyAsync<Exception>(() => dispatcher.DispatchUnlockAsync(null!));
    }
    #endregion

    private static OpenLockerRequest MaintenanceRequest(Locker locker, LockerCompartment compartment) => new(
        locker.Id,
        compartment.Id,
        null,
        null,
        null,
        null,
        LockerAccessType.Maintenance,
        LockerAccessMethod.SystemAuthorization,
        null,
        null);

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
