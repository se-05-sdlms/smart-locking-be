using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class MqttLockerListenerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleMessageAsync_ClosedPayload_UpdatesOnlineAndDoorClosed()
    {
        var dbName = Guid.NewGuid().ToString();
        var serviceProvider = BuildServiceProvider(dbName);
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();

        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LKR-01",
            Address = "Sảnh A",
            RecoveryAddress = "Lễ tân",
            DeviceIdentifier = "LKR-309228",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Offline,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "A01",
            HardwareCode = "HW-01",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Open,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        dbContext.Lockers.Add(locker);
        dbContext.LockerCompartments.Add(compartment);
        await dbContext.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var listener = new MqttLockerListenerService(config, scopeFactory, NullLogger<MqttLockerListenerService>.Instance);

        await listener.HandleMessageAsync("lockers/LKR-309228/doors/1/status", "CLOSED", CancellationToken.None);

        var updatedLocker = await dbContext.Lockers
            .AsNoTracking()
            .Include(l => l.Compartments)
            .SingleAsync(l => l.Id == locker.Id);
        var updatedComp = updatedLocker.Compartments.Single(c => c.HardwareChannel == 1);

        Assert.Equal(LockerConnectionStatus.Online, updatedLocker.ConnectionStatus);
        Assert.NotNull(updatedLocker.LastSeenAt);
        Assert.Equal(DoorStatus.Closed, updatedComp.DoorStatus);
    }

    [Fact]
    public async Task HandleMessageAsync_OpenPayload_UpdatesDoorOpen()
    {
        var dbName = Guid.NewGuid().ToString();
        var serviceProvider = BuildServiceProvider(dbName);
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();

        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LKR-02",
            Address = "Sảnh B",
            RecoveryAddress = "Lễ tân",
            DeviceIdentifier = "LKR-1A0844",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Offline,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "B01",
            HardwareCode = "HW-01",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        dbContext.Lockers.Add(locker);
        dbContext.LockerCompartments.Add(compartment);
        await dbContext.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var listener = new MqttLockerListenerService(config, scopeFactory, NullLogger<MqttLockerListenerService>.Instance);

        await listener.HandleMessageAsync("lockers/LKR-1A0844/doors/1/status", "OPEN", CancellationToken.None);

        var updatedComp = await dbContext.LockerCompartments.AsNoTracking().SingleAsync(c => c.Id == compartment.Id);
        Assert.Equal(DoorStatus.Open, updatedComp.DoorStatus);
    }

    [Fact]
    public async Task HandleMessageAsync_UnknownDevice_IgnoresSilently()
    {
        var dbName = Guid.NewGuid().ToString();
        var serviceProvider = BuildServiceProvider(dbName);

        var config = new ConfigurationBuilder().Build();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var listener = new MqttLockerListenerService(config, scopeFactory, NullLogger<MqttLockerListenerService>.Instance);

        // Không ném lỗi
        await listener.HandleMessageAsync("lockers/UNKNOWN/doors/1/status", "OPEN", CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessageAsync_OfflineBluetoothEvent_CreatesAccessEventAndAuditLog()
    {
        var dbName = Guid.NewGuid().ToString();
        var serviceProvider = BuildServiceProvider(dbName);
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();

        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LKR-B",
            Address = "Tòa B",
            RecoveryAddress = "Lễ tân",
            DeviceIdentifier = "LKR-1A0844",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Offline,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "B01",
            HardwareCode = "HW-01",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        dbContext.Lockers.Add(locker);
        dbContext.LockerCompartments.Add(compartment);
        await dbContext.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var listener = new MqttLockerListenerService(config, scopeFactory, NullLogger<MqttLockerListenerService>.Instance);

        string offlinePayload = "{\"deviceIdentifier\":\"LKR-1A0844\",\"channel\":1,\"accessMethod\":\"Bluetooth\",\"pinUsed\":\"606748\"}";
        await listener.HandleMessageAsync("lockers/LKR-1A0844/events/offline", offlinePayload, CancellationToken.None);

        var updatedLocker = await dbContext.Lockers.AsNoTracking().SingleAsync(l => l.Id == locker.Id);
        Assert.Equal(LockerConnectionStatus.Online, updatedLocker.ConnectionStatus);

        var accessEvent = await dbContext.LockerAccessEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.LockerCompartmentId == compartment.Id);
        Assert.NotNull(accessEvent);
        Assert.Equal(LockerAccessMethod.Bluetooth, accessEvent.AccessMethod);
        Assert.Equal(LockerAccessResult.Succeeded, accessEvent.Result);

        var auditLog = await dbContext.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EntityId == compartment.Id);
        Assert.NotNull(auditLog);
        Assert.Contains("Bluetooth", auditLog.Action);
    }

    private static IServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }
}
