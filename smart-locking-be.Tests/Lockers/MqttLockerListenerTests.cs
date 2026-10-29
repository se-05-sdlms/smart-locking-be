using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using smart_locking_be.Application.Exceptions;

namespace smart_locking_be.Tests.Lockers;

public sealed class MqttLockerListenerTests
{
    [Theory]
    [InlineData("*")]
    [InlineData("LKR-1A0844")]
    public async Task RemovedPhoneOfflineProxy_DoesNotAcceptResidentIdentity(string target)
    {
        using var provider = (ServiceProvider)BuildServiceProvider(Guid.NewGuid().ToString());
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var listener = new MqttLockerListenerService(MqttConfiguration(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MqttLockerListenerService>.Instance);
        var forged = $"r1|{target}|{Guid.NewGuid():N}|0900000001|{DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds()}";
        await listener.HandleMessageAsync("lockers/LKR-1A0844/workflow/request", $"1:N:{Guid.NewGuid():N}:{forged}", default);
        Assert.Empty(db.ReturnRequests); Assert.Empty(db.LockerAccessEvents); Assert.Empty(db.OfflineLockerEvents);
    }
    [Fact]
    public async Task MqttReturnWithoutAuthenticatedAllocation_IsKeptForReview_NotCreatedFromPhone()
    {
        using var provider = (ServiceProvider)BuildServiceProvider(Guid.NewGuid().ToString());
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B" };
        var slot = new LockerCompartment { Id = Guid.NewGuid(), Locker = locker, LockerId = locker.Id, Code = "B1", HardwareCode = "CH1", HardwareChannel = 1 };
        var owner = new User { Id = Guid.NewGuid(), PhoneNumber = "0900000001", Role = UserRole.Resident, Status = UserStatus.Active };
        db.AddRange(slot, new ResidentProfile { Id = Guid.NewGuid(), UserId = owner.Id, User = owner, FullName = "Resident" });
        await db.SaveChangesAsync();
        var eventId = Guid.NewGuid(); var subject = Guid.NewGuid(); long now = Now.ToUnixTimeSeconds();
        var listener = new MqttLockerListenerService(MqttConfiguration(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MqttLockerListenerService>.Instance, new FixedTimeProvider());
        await listener.HandleMessageAsync("lockers/LKR-B/events/offline",
            $"o1|{eventId:N}|{subject:N}|LKR-B|1|R|0900000001|123456|{now - 4}|{now - 3}|1000|1", default);
        Assert.Empty(db.ReturnRequests);
        Assert.DoesNotContain(db.LockerAccessEvents, item => item.Result == LockerAccessResult.Succeeded);
        Assert.Equal("NeedsReview", (await db.OfflineLockerEvents.SingleAsync()).Status);
        await db.Entry(slot).ReloadAsync(); // Adapter MQTT lưu bằng DbContext trong scope riêng.
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, slot.OperationalStatus);
    }
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MqttTopic_RejectsProofClaimingAnotherDevice()
    {
        using var provider = (ServiceProvider)BuildServiceProvider(Guid.NewGuid().ToString());
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var listener = new MqttLockerListenerService(MqttConfiguration(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MqttLockerListenerService>.Instance);
        var proof = $"o1|{Guid.NewGuid():N}|{Guid.NewGuid():N}|OTHER|1|R|0900000001|123456|0|0|1000|1";

        await Assert.ThrowsAsync<BadRequestException>(() => listener.HandleMessageAsync("lockers/LKR-B/events/offline", proof, default));

        Assert.Empty(db.OfflineLockerEvents); Assert.Empty(db.ReturnRequests); Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task MqttJournal_AppliesOrderedReturnCycle_AndIgnoresDuplicate()
    {
        using var provider = (ServiceProvider)BuildServiceProvider(Guid.NewGuid().ToString());
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B", OperationalStatus = LockerOperationalStatus.Operational };
        var slot = new LockerCompartment { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = "B1", HardwareCode = "CH1", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational };
        var user = new User { Id = Guid.NewGuid(), Role = UserRole.Resident, Status = UserStatus.Active, PhoneNumber = "0900000001" };
        db.AddRange(slot, new ResidentProfile { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Resident" });
        await db.SaveChangesAsync();
        var listener = new MqttLockerListenerService(MqttConfiguration(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MqttLockerListenerService>.Instance, new FixedTimeProvider());
        var subject = Guid.NewGuid();
        var resident = await db.ResidentProfiles.SingleAsync();
        // Luồng R hợp lệ đã được API xác thực và cấp ngăn trước; không tạo đơn từ r1 offline.
        db.ReturnRequests.Add(new ReturnRequest
        {
            Id = subject,
            ResidentProfileId = resident.Id,
            ResidentProfile = resident,
            LockerId = locker.Id,
            AllocatedCompartmentId = slot.Id,
            ReturnCode = "123456",
            Status = ReturnRequestStatus.Allocated
        });
        await db.SaveChangesAsync();
        long now = Now.ToUnixTimeSeconds();
        string deposit = $"o1|{Guid.NewGuid():N}|{subject:N}|LKR-B|1|R|0900000001|123456|{now - 4}|{now - 3}|1000|1";
        string pickup = $"o1|{Guid.NewGuid():N}|{subject:N}|LKR-B|1|G|0900000001|-|{now - 2}|{now - 1}|1000|2";

        await listener.HandleMessageAsync("lockers/LKR-B/events/offline", deposit, default);
        Assert.Equal(ReturnRequestStatus.Deposited, (await db.ReturnRequests.AsNoTracking().SingleAsync()).Status);
        await listener.HandleMessageAsync("lockers/LKR-B/events/offline", pickup, default);
        await listener.HandleMessageAsync("lockers/LKR-B/events/offline", pickup, default);

        Assert.Equal(ReturnRequestStatus.Completed, (await db.ReturnRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(2, await db.LockerAccessEvents.CountAsync());
        Assert.Equal(2, await db.OfflineLockerEvents.CountAsync());
        Assert.Equal(2, (await db.OfflineLockerCursors.SingleAsync()).LastSequence);
    }

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

        var config = MqttConfiguration();
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

        var config = MqttConfiguration();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var listener = new MqttLockerListenerService(config, scopeFactory, NullLogger<MqttLockerListenerService>.Instance);

        await listener.HandleMessageAsync("lockers/LKR-1A0844/doors/1/status", "OPEN", CancellationToken.None);

        var updatedComp = await dbContext.LockerCompartments.AsNoTracking().SingleAsync(c => c.Id == compartment.Id);
        Assert.Equal(DoorStatus.Open, updatedComp.DoorStatus);
        Assert.NotNull(updatedComp.DoorStatusObservedAt);
        var doorEvent = await dbContext.LockerEvents.SingleAsync(e => e.EventType == LockerEventType.DoorChanged);
        Assert.Equal(compartment.Id, doorEvent.LockerCompartmentId);
        Assert.Equal(nameof(DoorStatus.Closed), doorEvent.PreviousValue);
        Assert.Equal(nameof(DoorStatus.Open), doorEvent.NewValue);
    }

    [Fact]
    public async Task HandleMessageAsync_UnknownDevice_IgnoresSilently()
    {
        var dbName = Guid.NewGuid().ToString();
        var serviceProvider = BuildServiceProvider(dbName);

        var config = MqttConfiguration();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var listener = new MqttLockerListenerService(config, scopeFactory, NullLogger<MqttLockerListenerService>.Instance);

        // Không ném lỗi
        await listener.HandleMessageAsync("lockers/UNKNOWN/doors/1/status", "OPEN", CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessageAsync_UnsignedLegacyOfflineEvent_IsIgnored()
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

        var config = MqttConfiguration();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var listener = new MqttLockerListenerService(config, scopeFactory, NullLogger<MqttLockerListenerService>.Instance);

        string offlinePayload = "{\"deviceIdentifier\":\"LKR-1A0844\",\"channel\":1,\"accessMethod\":\"Bluetooth\",\"pinUsed\":\"606748\"}";
        await listener.HandleMessageAsync("lockers/LKR-1A0844/events/offline", offlinePayload, CancellationToken.None);

        var updatedLocker = await dbContext.Lockers.AsNoTracking().SingleAsync(l => l.Id == locker.Id);
        Assert.Equal(LockerConnectionStatus.Offline, updatedLocker.ConnectionStatus);

        var accessEvent = await dbContext.LockerAccessEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.LockerCompartmentId == compartment.Id);
        Assert.Null(accessEvent);

        var auditLog = await dbContext.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EntityId == compartment.Id);
        Assert.Null(auditLog);
    }

    [Theory]
    [InlineData(LockerAccessMethod.GuestSession)]
    [InlineData(LockerAccessMethod.RemoteApp)]
    [InlineData(LockerAccessMethod.Bluetooth)]
    public async Task HandleMessageAsync_ClosedDoorWithoutWorkflowReceipt_DoesNotCompletePickup(LockerAccessMethod method)
    {
        var services = new ServiceCollection();
        // Chia sẻ cùng database giữa các scope của listener.
        string database = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database));
        services.AddSingleton<smart_locking_be.Application.Interfaces.Services.IBluetoothMessageCodec, BluetoothProtocol>();
        RegisterMonitor(services);
        using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var locker = new Locker { Id = Guid.NewGuid(), Code = "LKR-B", DeviceIdentifier = "LKR-1A0844", Address = "B", RecoveryAddress = "B" };
        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "B-01",
            HardwareCode = "CH1",
            HardwareChannel = 1,
            DoorStatus = DoorStatus.Open
        };
        var request = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            AllocatedCompartmentId = compartment.Id,
            Status = ReturnRequestStatus.Deposited,
            ReturnCode = "123456"
        };
        db.AddRange(locker, compartment, request, new LockerAccessEvent
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            LockerCompartmentId = compartment.Id,
            ReturnRequestId = request.Id,
            AccessType = LockerAccessType.ShipperReturnPickup,
            AccessMethod = method,
            Result = LockerAccessResult.Succeeded,
            OccurredAt = Now
        });
        await db.SaveChangesAsync();
        var listener = new MqttLockerListenerService(MqttConfiguration(),
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MqttLockerListenerService>.Instance);

        await listener.HandleMessageAsync("lockers/LKR-1A0844/doors/1/status", "CLOSED", CancellationToken.None);

        await db.Entry(compartment).ReloadAsync();
        await db.Entry(request).ReloadAsync();
        Assert.Equal(DoorStatus.Closed, compartment.DoorStatus);
        Assert.Equal(ReturnRequestStatus.Deposited, request.Status);
        Assert.Null(request.ShipperPickedUpAt);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HandleMessageAsync_RetainedOrOldDoorSignal_DoesNotRestoreOnline(bool retained, bool stale)
    {
        using var provider = (ServiceProvider)BuildServiceProvider(Guid.NewGuid().ToString());
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LKR-B",
            DeviceIdentifier = "LKR-1A0844",
            Address = "B",
            RecoveryAddress = "B",
            ConnectionStatus = LockerConnectionStatus.Offline
        };
        var slot = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "B-01",
            HardwareCode = "CH1",
            HardwareChannel = 1,
            DoorStatus = DoorStatus.Closed,
            DoorStatusObservedAt = Now
        };
        db.Add(slot);
        await db.SaveChangesAsync();
        var listener = new MqttLockerListenerService(MqttConfiguration(),
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MqttLockerListenerService>.Instance,
            new FixedTimeProvider());
        string payload = stale
            ? System.Text.Json.JsonSerializer.Serialize(new { status = "OPEN", occurredAt = Now.AddSeconds(-1) })
            : "OPEN";

        await listener.HandleMessageAsync("lockers/LKR-1A0844/doors/1/status", payload, CancellationToken.None, retained);

        await db.Entry(slot).ReloadAsync();
        await db.Entry(locker).ReloadAsync();
        Assert.Equal(DoorStatus.Closed, slot.DoorStatus);
        Assert.Equal(Now, slot.DoorStatusObservedAt);
        Assert.Equal(LockerConnectionStatus.Offline, locker.ConnectionStatus);
        Assert.Empty(await db.LockerEvents.ToListAsync());
    }

    private static IConfiguration MqttConfiguration() => new ConfigurationBuilder().Build();

    private static IServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        RegisterMonitor(services);
        return services.BuildServiceProvider();
    }

    private static void RegisterMonitor(IServiceCollection services)
    {
        services.AddSingleton<IBluetoothMessageCodec, BluetoothProtocol>();
        services.AddScoped<IOfflinePinService, OfflinePinService>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider());
        services.AddSingleton<IOperationsRealtimeNotifier, NoopRealtime>();
        services.AddScoped<LockerConnectionMonitor>();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoopRealtime : IOperationsRealtimeNotifier
    {
        public Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
