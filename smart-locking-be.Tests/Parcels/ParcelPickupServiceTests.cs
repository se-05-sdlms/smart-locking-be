using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelPickupServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnlockAsync_OwnedParcel_DispatchesResidentPickup()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (User resident, Parcel parcel, LockerCompartment compartment) = await SeedParcelAsync(dbContext);
        var lockerAccess = new RecordingLockerAccessService();
        var service = new ParcelPickupService(dbContext, lockerAccess, new FixedTimeProvider(Now));

        var response = await service.UnlockAsync(
            resident.Id,
            parcel.Id,
            "127.0.0.1",
            "mobile");

        OpenLockerRequest request = Assert.Single(lockerAccess.Requests);
        Assert.Equal(parcel.Id, request.ParcelId);
        Assert.Equal(compartment.Id, request.LockerCompartmentId);
        Assert.Equal(LockerAccessType.ResidentPickup, request.AccessType);
        Assert.Equal(LockerAccessMethod.RemoteApp, request.AccessMethod);
        Assert.Equal(LockerAccessResult.Succeeded, response.Result);
    }

    [Fact]
    public async Task UnlockAsync_OutstandingCharge_IsBlockedBeforeDispatch()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (User resident, Parcel parcel, _) = await SeedParcelAsync(dbContext, outstandingCharge: true);
        var lockerAccess = new RecordingLockerAccessService();
        var service = new ParcelPickupService(dbContext, lockerAccess, TimeProvider.System);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UnlockAsync(resident.Id, parcel.Id, null, null));

        Assert.Contains("phí quá hạn", exception.Message);
        Assert.Empty(lockerAccess.Requests);
    }

    [Fact]
    public async Task UnlockAsync_OtherResident_IsForbidden()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (_, Parcel parcel, _) = await SeedParcelAsync(dbContext);
        var service = new ParcelPickupService(
            dbContext,
            new RecordingLockerAccessService(),
            TimeProvider.System);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UnlockAsync(Guid.NewGuid(), parcel.Id, null, null));
    }

    [Fact]
    public async Task ConfirmPickupAsync_ValidAccess_MarksParcelRetrieved()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        (User resident, Parcel parcel, LockerCompartment compartment) = await SeedParcelAsync(dbContext);
        LockerAccessEvent accessEvent = new()
        {
            Id = Guid.NewGuid(),
            LockerId = compartment.LockerId,
            LockerCompartmentId = compartment.Id,
            UserId = resident.Id,
            ParcelId = parcel.Id,
            AccessType = LockerAccessType.ResidentPickup,
            AccessMethod = LockerAccessMethod.RemoteApp,
            Result = LockerAccessResult.Succeeded,
            OccurredAt = Now.AddMinutes(-1),
        };
        dbContext.LockerAccessEvents.Add(accessEvent);
        await dbContext.SaveChangesAsync();
        var service = new ParcelPickupService(
            dbContext,
            new RecordingLockerAccessService(),
            new FixedTimeProvider(Now));

        var response = await service.ConfirmPickupAsync(resident.Id, accessEvent.Id);

        Assert.Equal(ParcelStatus.Retrieved, response.Status);
        Assert.Equal(Now, response.RetrievedAt);
        Assert.Equal(Now, parcel.RetrievedAt);
        ParcelStatusHistory history = await dbContext.ParcelStatusHistories.SingleAsync();
        Assert.Equal(ParcelStatus.Stored, history.FromStatus);
        Assert.Equal(ParcelStatus.Retrieved, history.ToStatus);
        Assert.Equal(resident.Id, history.ChangedByUserId);
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(User Resident, Parcel Parcel, LockerCompartment Compartment)> SeedParcelAsync(
        ApplicationDbContext dbContext,
        bool outstandingCharge = false)
    {
        User administrator = new()
        {
            Id = Guid.NewGuid(),
            Email = "admin@boxora.local",
            PasswordHash = "hash",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        User resident = new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0901234567",
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        ResidentProfile profile = new()
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            User = resident,
            FullName = "Cư Dân",
            DeliveryApprovalMode = DeliveryApprovalMode.Manual,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Locker locker = new()
        {
            Id = Guid.NewGuid(),
            Code = "L-01",
            Address = "Tòa A",
            RecoveryAddress = "Quầy lễ tân",
            DeviceIdentifier = "esp32-l01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
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
        SystemPolicy policy = new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            EnableRemoteUnlock = true,
            Currency = "VND",
            CreatedByUserId = administrator.Id,
            CreatedByUser = administrator,
            CreatedAt = Now,
        };
        DeliveryRequest delivery = new()
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = profile.Id,
            ResidentProfile = profile,
            LockerId = locker.Id,
            Locker = locker,
            SystemPolicyId = policy.Id,
            SystemPolicy = policy,
            AllocatedCompartmentId = compartment.Id,
            AllocatedCompartment = compartment,
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.Deposited,
            SessionExpiresAt = Now,
            LastActivityAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Parcel parcel = new()
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = delivery.Id,
            DeliveryRequest = delivery,
            ParcelCode = "P-001",
            Status = ParcelStatus.Stored,
            StoredAt = Now.AddHours(-1),
            PickupDueAt = Now.AddHours(23),
            MaxStorageUntil = Now.AddDays(3),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        if (outstandingCharge)
        {
            parcel.OverdueCharge = new OverdueCharge
            {
                Id = Guid.NewGuid(),
                ParcelId = parcel.Id,
                Parcel = parcel,
                Amount = 10_000,
                RatePerHourSnapshot = 5_000,
                Currency = "VND",
                ChargeStartAt = Now.AddHours(-2),
                CalculatedThrough = Now,
                Status = OverdueChargeStatus.Outstanding,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
        }

        dbContext.AddRange(administrator, resident, profile, locker, compartment, policy, delivery, parcel);
        await dbContext.SaveChangesAsync();
        return (resident, parcel, compartment);
    }

    private sealed class RecordingLockerAccessService : ILockerAccessService
    {
        public List<OpenLockerRequest> Requests { get; } = [];

        public Task<OpenLockerResponse> OpenAsync(
            OpenLockerRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new OpenLockerResponse(
                Guid.NewGuid(),
                request.LockerId,
                request.LockerCompartmentId,
                "esp32-l01",
                1,
                LockerAccessResult.Succeeded,
                null,
                Now));
        }

        public Task<SyncOfflineAccessResponse> SyncOfflineEventsAsync(SyncOfflineAccessRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new SyncOfflineAccessResponse(request.Events.Count, request.Events.Count, []));
        public Task<ConfigureCompartmentPinResponse> ConfigurePinAsync(Guid lockerId, Guid compartmentId, ConfigureCompartmentPinRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ConfigureCompartmentPinResponse(lockerId, compartmentId, "esp32-l01", 1, request.PinCode, true, Now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
