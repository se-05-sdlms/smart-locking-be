using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
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

    [Theory]
    [InlineData("Administrator", false, false, true)]
    [InlineData("LockerOperator", true, false, true)]
    [InlineData("LockerOperator", false, false, false)]
    [InlineData("LockerOperator", true, true, false)]
    [InlineData("Resident", true, false, false)]
    public async Task UpdateLockerStatus_EnforcesScopeAndRecordsActor(string role, bool assigned, bool revoked, bool allowed)
    {
        await using var db = CreateDbContext();
        var (locker, slot) = await SeedLockerAsync(db);
        slot.OperationalStatus = LockerCompartmentOperationalStatus.OutOfService;
        var actor = Guid.NewGuid();
        if (assigned)
            db.Add(new OperatorAssignment { Id = Guid.NewGuid(), OperatorUserId = actor, LockerId = locker.Id, RevokedAt = revoked ? Now : null });
        await db.SaveChangesAsync();
        var service = new LockerService(db);

        if (!allowed)
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateLockerStatusAsync(actor, role,
                locker.Id, new(LockerOperationalStatus.OutOfService, "Kiểm tra tủ")));
            Assert.Equal(LockerOperationalStatus.Operational, locker.OperationalStatus);
            Assert.Empty(db.AuditLogs);
            Assert.Empty(db.LockerEvents);
            return;
        }

        await service.UpdateLockerStatusAsync(actor, role, locker.Id, new(LockerOperationalStatus.OutOfService, "  Kiểm tra tủ  "));
        var audit = await db.AuditLogs.SingleAsync();
        var change = await db.LockerEvents.SingleAsync();
        Assert.Equal(actor, audit.ActorUserId);
        Assert.Equal(actor, change.ActorUserId);
        Assert.Equal("Kiểm tra tủ", change.Reason);
        Assert.Equal(nameof(LockerOperationalStatus.Operational), change.PreviousValue);
        Assert.Equal(nameof(LockerOperationalStatus.OutOfService), change.NewValue);
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, slot.OperationalStatus);
        Assert.Equal(DoorStatus.Closed, slot.DoorStatus);
    }

    [Theory]
    [InlineData(LockerOperationalStatus.OutOfService, " ")]
    [InlineData((LockerOperationalStatus)999, "Kiểm tra tủ")]
    public async Task UpdateLockerStatus_RejectsInvalidInputWithoutChanges(LockerOperationalStatus status, string reason)
    {
        await using var db = CreateDbContext();
        var (locker, _) = await SeedLockerAsync(db);

        await Assert.ThrowsAsync<BadRequestException>(() => new LockerService(db).UpdateLockerStatusAsync(
            Guid.NewGuid(), "Administrator", locker.Id, new(status, reason)));

        Assert.Equal(LockerOperationalStatus.Operational, locker.OperationalStatus);
        Assert.Empty(db.AuditLogs);
        Assert.Empty(db.LockerEvents);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("         ")]
    public async Task EmergencyUnlock_RejectsShortReasonBeforeDispatch(string reason)
    {
        await using var db = CreateDbContext();
        var (locker, slot) = await SeedLockerAsync(db);
        var dispatcher = new RecordingDispatcher();
        var service = new EmergencyUnlockService(db,
            new LockerAccessService(db, dispatcher, TimeProvider.System), TimeProvider.System);

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(Guid.NewGuid(), "Administrator",
            new(locker.Id, slot.Id, null, reason)));

        Assert.Empty(dispatcher.Commands);
        Assert.Empty(db.EmergencyUnlocks);
    }

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
        var service = new LockerAccessService(dbContext, dispatcher, TimeProvider.System,
            configuration: MonitoringConfiguration());

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.OpenAsync(MaintenanceRequest(locker, compartment)));

        Assert.Equal(LockerErrorCodes.Offline, exception.ErrorCode);
        Assert.Empty(dispatcher.Commands);
    }

    [Fact]
    public async Task OpenAsync_MonitoringDisabled_DoesNotBlockLegacyLocker()
    {
        await using ApplicationDbContext db = CreateDbContext();
        var (locker, slot) = await SeedLockerAsync(db, LockerConnectionStatus.Offline);
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(db, dispatcher, TimeProvider.System);

        var response = await service.OpenAsync(MaintenanceRequest(locker, slot));

        Assert.Equal(LockerAccessResult.Succeeded, response.Result);
        Assert.Single(dispatcher.Commands);
    }

    [Fact]
    public async Task OpenAsync_OfflineLockerWithMonitoring_AllowsBluetoothWithoutMqttDispatch()
    {
        await using ApplicationDbContext db = CreateDbContext();
        var (locker, slot) = await SeedLockerAsync(db, LockerConnectionStatus.Offline);
        var resident = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FullName = "Resident",
            User = new User { Id = Guid.NewGuid(), PhoneNumber = "0900000001" }
        };
        resident.UserId = resident.User.Id;
        var request = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            ResidentProfile = resident,
            ResidentProfileId = resident.Id,
            ReturnCode = "123456"
        };
        db.Add(request);
        await db.SaveChangesAsync();
        var dispatcher = new RecordingDispatcher();
        var service = new LockerAccessService(db, dispatcher, new FixedTimeProvider(Now),
            new BluetoothProtocol(), MonitoringConfiguration());

        var response = await service.OpenAsync(new OpenLockerRequest(locker.Id, slot.Id,
            resident.UserId, null, null, request.Id, LockerAccessType.ResidentReturnDropOff,
            LockerAccessMethod.Bluetooth, null, null));

        Assert.Equal(LockerAccessResult.Succeeded, response.Result);
        Assert.NotNull(response.Bluetooth);
        Assert.StartsWith("c1|", response.Bluetooth.Command);
        Assert.Empty(dispatcher.Commands);
    }

    private static IConfiguration MonitoringConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["LockerMonitoring:Enabled"] = "true" })
        .Build();

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
