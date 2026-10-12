using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class EmergencyUnlockServiceTests
{
    private sealed class FakeLockerAccessService : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OpenLockerResponse(
                Guid.NewGuid(),
                request.LockerId,
                request.LockerCompartmentId,
                "DEV-01",
                1,
                LockerAccessResult.Succeeded,
                null,
                DateTimeOffset.UtcNow));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    #region EMG-01: CreateAsync
    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-01",
            Address = "123 Street",
            RecoveryAddress = "Recovery",
            DeviceIdentifier = "DEV-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var comp = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Code = "C01",
            HardwareCode = "HW-01",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational
        };
        dbContext.AddRange(locker, comp);
        await dbContext.SaveChangesAsync();

        var service = new EmergencyUnlockService(dbContext, new FakeLockerAccessService(), TimeProvider.System);
        var adminId = Guid.NewGuid();

        EmergencyUnlockResponse response = await service.CreateAsync(
            adminId,
            nameof(UserRole.Administrator),
            new EmergencyUnlockRequest(locker.Id, comp.Id, null, "Fire drill test"),
            default);

        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);
        Assert.Single(dbContext.EmergencyUnlocks);
    }

    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new EmergencyUnlockService(dbContext, new FakeLockerAccessService(), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            new EmergencyUnlockRequest(Guid.NewGuid(), Guid.NewGuid(), null, ""),
            default));
    }

    [Fact]
    [Trait("Category", "EMG-01")]
    public async Task CreateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-01",
            Address = "123 Street",
            RecoveryAddress = "Recovery",
            DeviceIdentifier = "DEV-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = new EmergencyUnlockService(dbContext, new FakeLockerAccessService(), TimeProvider.System);

        // Compartment does not exist in locker
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            new EmergencyUnlockRequest(locker.Id, Guid.NewGuid(), null, "Valid reason"),
            default));
    }
    #endregion
}
