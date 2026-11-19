using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class EmergencyUnlockServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeLockerAccessService(LockerAccessResult result = LockerAccessResult.Succeeded) : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OpenLockerResponse(
                Guid.NewGuid(),
                request.LockerId,
                request.LockerCompartmentId,
                "DEV-01",
                1,
                result,
                null,
                DateTimeOffset.UtcNow
            ));
        }
    }

    [Fact]
    public async Task CreateAsync_WhenPackageActionIsHandover_SetsParcelToRetrievedAndReleasesCompartment()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, parcel) = await SeedLockerWithParcelAsync(db);

        var service = new EmergencyUnlockService(db, new FakeLockerAccessService(), TimeProvider.System);
        var request = new EmergencyUnlockRequest(
            locker.Id,
            compartment.Id,
            null,
            "Cư dân có mặt lấy hàng khẩn cấp",
            "Handover"
        );

        var response = await service.CreateAsync(adminUser.Id, nameof(UserRole.Administrator), request);

        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);

        var updatedParcel = await db.Parcels.Include(p => p.DeliveryRequest).SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Retrieved, updatedParcel.Status);
        Assert.NotNull(updatedParcel.RetrievedAt);
        Assert.NotNull(updatedParcel.DeliveryRequest.CompartmentReleasedAt);

        var history = await db.ParcelStatusHistories.SingleOrDefaultAsync(h => h.ParcelId == parcel.Id);
        Assert.NotNull(history);
        Assert.Equal(ParcelStatus.Retrieved, history.ToStatus);
    }

    [Fact]
    public async Task CreateAsync_WhenPackageActionIsRemove_SetsParcelToRemovedAndRecordsReason()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, parcel) = await SeedLockerWithParcelAsync(db);

        var service = new EmergencyUnlockService(db, new FakeLockerAccessService(), TimeProvider.System);
        var request = new EmergencyUnlockRequest(
            locker.Id,
            compartment.Id,
            null,
            "Hàng hỏng bốc mùi thu hồi về kho BQL",
            "Remove"
        );

        var response = await service.CreateAsync(adminUser.Id, nameof(UserRole.Administrator), request);

        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);

        var updatedParcel = await db.Parcels.Include(p => p.DeliveryRequest).SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Removed, updatedParcel.Status);
        Assert.NotNull(updatedParcel.RemovedAt);
        Assert.Equal(adminUser.Id, updatedParcel.RemovedByUserId);
        Assert.Equal("Hàng hỏng bốc mùi thu hồi về kho BQL", updatedParcel.RemovalReason);
        Assert.NotNull(updatedParcel.DeliveryRequest.CompartmentReleasedAt);

        var history = await db.ParcelStatusHistories.SingleOrDefaultAsync(h => h.ParcelId == parcel.Id);
        Assert.NotNull(history);
        Assert.Equal(ParcelStatus.Removed, history.ToStatus);
    }

    [Fact]
    public async Task CreateAsync_WhenPackageActionIsKeep_LeavesParcelAsStored()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, parcel) = await SeedLockerWithParcelAsync(db);

        var service = new EmergencyUnlockService(db, new FakeLockerAccessService(), TimeProvider.System);
        var request = new EmergencyUnlockRequest(
            locker.Id,
            compartment.Id,
            null,
            "Chỉ kiểm tra bảo trì chốt khóa ngăn",
            "Keep"
        );

        var response = await service.CreateAsync(adminUser.Id, nameof(UserRole.Administrator), request);

        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);

        var updatedParcel = await db.Parcels.Include(p => p.DeliveryRequest).SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Stored, updatedParcel.Status);
        Assert.Null(updatedParcel.RetrievedAt);
        Assert.Null(updatedParcel.RemovedAt);
        Assert.Null(updatedParcel.DeliveryRequest.CompartmentReleasedAt);
    }

    [Fact]
    public async Task CreateAsync_WhenSetOutOfServiceIsTrue_SetsCompartmentToOutOfService()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, _) = await SeedLockerWithParcelAsync(db);

        var service = new EmergencyUnlockService(db, new FakeLockerAccessService(), TimeProvider.System);
        var request = new EmergencyUnlockRequest(
            locker.Id,
            compartment.Id,
            null,
            "Chốt cửa ngăn hỏng cần bảo trì cách ly",
            "Keep",
            SetOutOfService: true
        );

        var response = await service.CreateAsync(adminUser.Id, nameof(UserRole.Administrator), request);

        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);

        var updatedCompartment = await db.LockerCompartments.SingleAsync(c => c.Id == compartment.Id);
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, updatedCompartment.OperationalStatus);
    }

    private static async Task<(User Admin, Locker Locker, LockerCompartment Compartment, Parcel Parcel)> SeedLockerWithParcelAsync(ApplicationDbContext db)
    {
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0900000001",
            PasswordHash = "hash",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Users.Add(adminUser);

        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "L-01",
            Address = "123 Main St",
            DeviceIdentifier = "DEV-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Lockers.Add(locker);

        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            HardwareChannel = 1,
            HardwareCode = "C-01",
            Code = "C-01",
            DoorStatus = DoorStatus.Closed,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.LockerCompartments.Add(compartment);

        var delivery = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            AllocatedCompartmentId = compartment.Id,
            Status = DeliveryRequestStatus.Deposited,
            GuestSessionTokenHash = "hash",
            RecipientPhoneSnapshot = "0912345678",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.DeliveryRequests.Add(delivery);

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = delivery.Id,
            ParcelCode = "P-TEST-01",
            Status = ParcelStatus.Stored,
            StoredAt = Now,
            PickupDueAt = Now.AddDays(2),
            MaxStorageUntil = Now.AddDays(7),
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Parcels.Add(parcel);

        await db.SaveChangesAsync();
        return (adminUser, locker, compartment, parcel);
    }

    private static ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .Options);
}
