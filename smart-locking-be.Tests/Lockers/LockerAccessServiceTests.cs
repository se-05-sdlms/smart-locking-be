using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task OpenAsync_OperationalLocker_DispatchesAndRecordsSuccess()
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
    public async Task OpenAsync_OfflineLocker_BlocksWithoutDispatching()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(
            dbContext,
            LockerConnectionStatus.Offline);
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(dbContext, dispatcher, TimeProvider.System);

        OpenLockerResponse response = await service.OpenAsync(MaintenanceRequest(locker, compartment));

        Assert.Equal(LockerAccessResult.Blocked, response.Result);
        Assert.Contains("không trực tuyến", response.FailureReason);
        Assert.Empty(dispatcher.Commands);
    }

    [Fact]
    public async Task OpenAsync_DispatchFailure_RecordsFailure()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (Locker locker, LockerCompartment compartment) = await SeedLockerAsync(dbContext);
        var service = new LockerAccessService(dbContext, new FailingDispatcher(), TimeProvider.System);

        OpenLockerResponse response = await service.OpenAsync(MaintenanceRequest(locker, compartment));

        Assert.Equal(LockerAccessResult.Failed, response.Result);
        Assert.Equal(response.FailureReason, (await dbContext.LockerAccessEvents.SingleAsync()).FailureReason);
    }

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
