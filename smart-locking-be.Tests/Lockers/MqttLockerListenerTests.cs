using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.Interfaces.Services;
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
    public async Task HandleMessageAsync_OpenToClosed_FinalizesDeliveryOnlyOnce()
    {
        var dbName = Guid.NewGuid().ToString();
        var deliveryService = new RecordingDeliveryRequestService();
        var serviceProvider = BuildServiceProvider(dbName, deliveryService);
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        Guid requestId = Guid.NewGuid();
        var locker = new Locker
        {
            Id = Guid.NewGuid(), Code = "LKR-03", Address = "Sảnh C", RecoveryAddress = "Lễ tân",
            DeviceIdentifier = "LKR-FINALIZE", OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now, UpdatedAt = Now,
        };
        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = "C01", HardwareCode = "HW-01",
            HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Open, CreatedAt = Now, UpdatedAt = Now,
        };
        dbContext.AddRange(locker, compartment, new LockerAccessEvent
        {
            Id = Guid.NewGuid(), LockerId = locker.Id, LockerCompartmentId = compartment.Id,
            DeliveryRequestId = requestId, AccessType = LockerAccessType.ShipperDropOff,
            AccessMethod = LockerAccessMethod.GuestSession, Result = LockerAccessResult.Succeeded,
            OccurredAt = Now,
        });
        await dbContext.SaveChangesAsync();

        var listener = new MqttLockerListenerService(
            new ConfigurationBuilder().Build(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MqttLockerListenerService>.Instance);

        await listener.HandleMessageAsync("lockers/LKR-FINALIZE/doors/1/status", "CLOSED", CancellationToken.None);
        await listener.HandleMessageAsync("lockers/LKR-FINALIZE/doors/1/status", "CLOSED", CancellationToken.None);

        Assert.Equal([requestId], deliveryService.FinalizedRequestIds);
    }

    private static IServiceProvider BuildServiceProvider(
        string dbName,
        IDeliveryRequestService? deliveryRequestService = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        if (deliveryRequestService is not null)
            services.AddSingleton(deliveryRequestService);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingDeliveryRequestService : IDeliveryRequestService
    {
        public List<Guid> FinalizedRequestIds { get; } = [];
        public Task FinalizeDropOffAsync(Guid requestId, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            FinalizedRequestIds.Add(requestId);
            return Task.CompletedTask;
        }

        public Task<InitiateDeliveryResponse> CreateAsync(InitiateDeliveryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryRequestSummaryResponse> SubmitAsync(Guid id, string guestSessionToken, SubmitDeliveryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GuestDeliveryStatusResponse> GetAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ExpireStartedSessionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<PendingDeliveryRequestResponse>> GetPendingRequestsForResidentAsync(Guid residentUserId, CancellationToken cancellationToken = default, int pageNumber = 1, int pageSize = 20) => throw new NotSupportedException();
        public Task<DeliveryRequestSummaryResponse> ApproveDeliveryRequestAsync(Guid residentUserId, Guid requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryRequestSummaryResponse> RejectDeliveryRequestAsync(Guid residentUserId, Guid requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ExpirePendingApprovalsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CompartmentReservationResponse> OpenCompartmentAsync(Guid requestId, string guestSessionToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
