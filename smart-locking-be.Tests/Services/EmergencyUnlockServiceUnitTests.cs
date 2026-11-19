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
    public async Task CreateAsync_SendsUnlockCommand_AndLeavesParcelUntouched()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, parcel) = await SeedLockerWithParcelAsync(db);

        var service = new EmergencyUnlockService(db, new FakeLockerAccessService(), TimeProvider.System);
        var request = new EmergencyUnlockRequest(
            locker.Id,
            compartment.Id,
            null,
            "Mở kiểm tra cửa bị kẹt phần cứng"
        );

        var response = await service.CreateAsync(adminUser.Id, nameof(UserRole.Administrator), request);

        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);
        Assert.Equal("A01", response.CompartmentCode);

        // Parcel remains Stored, no status change, no premature compartment release
        var updatedParcel = await db.Parcels.Include(p => p.DeliveryRequest).SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Stored, updatedParcel.Status);
        Assert.Null(updatedParcel.RetrievedAt);
        Assert.Null(updatedParcel.DeliveryRequest.CompartmentReleasedAt);

        // Audit log exists
        var audit = await db.AuditLogs.SingleOrDefaultAsync(a => a.Action == "Locker.EmergencyUnlock");
        Assert.NotNull(audit);
        Assert.Equal(AuditLogResult.Succeeded, audit.Result);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(User User, Locker Locker, LockerCompartment Compartment, Parcel Parcel)> SeedLockerWithParcelAsync(ApplicationDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Users.Add(user);

        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LCK-01",
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
            Code = "A01",
            HardwareCode = "HW-01",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.LockerCompartments.Add(compartment);

        var deliveryRequest = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            AllocatedCompartmentId = compartment.Id,
            Status = DeliveryRequestStatus.Deposited,
            AllocatedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.DeliveryRequests.Add(deliveryRequest);

        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryRequest.Id,
            ParcelCode = "PKG123456",
            Status = ParcelStatus.Stored,
            StoredAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Parcels.Add(parcel);

        var reservation = new CompartmentReservation
        {
            Id = Guid.NewGuid(),
            LockerCompartmentId = compartment.Id,
            DeliveryRequestId = deliveryRequest.Id,
            ReservedAt = Now,
            ExpiresAt = Now.AddDays(3),
            ReleasedAt = null
        };
        db.CompartmentReservations.Add(reservation);

        await db.SaveChangesAsync();
        return (user, locker, compartment, parcel);
    }
}
