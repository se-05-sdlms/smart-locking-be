using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class OfflineUnlockGuardTests
{
    [Theory]
    [InlineData(LockerConnectionStatus.Offline)]
    [InlineData(LockerConnectionStatus.Unknown)]
    public async Task OfflineOrUnknownDoesNotPublishOrPersistSuccessfulAccess(LockerConnectionStatus status)
    {
        await using var db = Database();
        var compartment = Compartment(status);
        db.Add(compartment);
        await db.SaveChangesAsync();
        var dispatcher = new Dispatcher();
        var service = new LockerAccessService(db, dispatcher, TimeProvider.System);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.OpenAsync(Request(compartment)));
        Assert.Equal("locker/offline", error.ErrorCode);
        Assert.Equal(0, dispatcher.Commands);
        Assert.Empty(db.LockerAccessEvents);
    }

    [Fact]
    public async Task OnlineEmergencyAccessStillPublishesOneCorrelatedCommand()
    {
        await using var db = Database();
        var compartment = Compartment(LockerConnectionStatus.Online);
        db.Add(compartment);
        await db.SaveChangesAsync();
        var dispatcher = new Dispatcher();
        var service = new LockerAccessService(db, dispatcher, TimeProvider.System);
        var response = await service.OpenAsync(Request(compartment));
        Assert.Equal(LockerAccessResult.Succeeded, response.Result);
        Assert.Equal(1, dispatcher.Commands);
        Assert.Equal(response.AccessEventId, dispatcher.LastCommandId);
        Assert.Equal(response.AccessEventId, Assert.Single(db.LockerAccessEvents).Id);
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static LockerCompartment Compartment(LockerConnectionStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Code = "C01",
        HardwareChannel = 1,
        OperationalStatus = LockerCompartmentOperationalStatus.Operational,
        Locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "TEST",
            DeviceIdentifier = "TEST-DEVICE",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = status
        }
    };

    private static OpenLockerRequest Request(LockerCompartment compartment) => new(
        compartment.LockerId, compartment.Id, Guid.NewGuid(), null, null, null,
        LockerAccessType.OperatorEmergency, LockerAccessMethod.OperatorAuthorization, null, "test");

    private sealed class Dispatcher : ILockerCommandDispatcher
    {
        public int Commands { get; private set; }
        public Guid LastCommandId { get; private set; }
        public Task DispatchUnlockAsync(LockerUnlockCommand command, CancellationToken cancellationToken = default)
        {
            Commands++;
            LastCommandId = command.CommandId;
            return Task.CompletedTask;
        }
    }
}
