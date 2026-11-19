using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Services;

public sealed class ConfirmCompartmentPackageActionUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handover_Parcel_SetsRetrieved_AndReleasesReservationAndCompartment()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, parcel, reservation) = await SeedLockerWithParcelAsync(db);

        var service = new LockerService(db);
        var request = new ConfirmCompartmentPackageActionRequest("Handover", "Bàn giao tận tay cư dân");

        var response = await service.ConfirmCompartmentPackageActionAsync(
            adminUser.Id, nameof(UserRole.Administrator), locker.Id, compartment.Id, request);

        Assert.Equal("Handover", response.Action);
        Assert.Equal(parcel.ParcelCode, response.PackageCode);

        // Verify Parcel
        var updatedParcel = await db.Parcels.Include(p => p.DeliveryRequest).SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Retrieved, updatedParcel.Status);
        Assert.NotNull(updatedParcel.RetrievedAt);
        Assert.NotNull(updatedParcel.DeliveryRequest.CompartmentReleasedAt);

        // Verify Reservation
        var updatedReservation = await db.CompartmentReservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.NotNull(updatedReservation.ReleasedAt);

        // Verify Status History & Audit
        var history = await db.ParcelStatusHistories.SingleOrDefaultAsync(h => h.ParcelId == parcel.Id);
        Assert.NotNull(history);
        Assert.Equal(ParcelStatus.Retrieved, history.ToStatus);
    }

    [Fact]
    public async Task Remove_Parcel_SetsRemoved_AndReleasesReservationAndCompartment()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, parcel, reservation) = await SeedLockerWithParcelAsync(db);

        var service = new LockerService(db);
        var request = new ConfirmCompartmentPackageActionRequest("Remove", "Hàng hỏng thu hồi về kho");

        var response = await service.ConfirmCompartmentPackageActionAsync(
            adminUser.Id, nameof(UserRole.Administrator), locker.Id, compartment.Id, request);

        Assert.Equal("Remove", response.Action);

        var updatedParcel = await db.Parcels.Include(p => p.DeliveryRequest).SingleAsync(p => p.Id == parcel.Id);
        Assert.Equal(ParcelStatus.Removed, updatedParcel.Status);
        Assert.NotNull(updatedParcel.RemovedAt);
        Assert.Equal("Hàng hỏng thu hồi về kho", updatedParcel.RemovalReason);
        Assert.NotNull(updatedParcel.DeliveryRequest.CompartmentReleasedAt);

        var updatedReservation = await db.CompartmentReservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.NotNull(updatedReservation.ReleasedAt);
    }

    [Fact]
    public async Task Handover_ReturnRequest_SetsCompleted_AndReleasesCompartmentReleasedAtAndReservation()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, returnReq, reservation) = await SeedLockerWithReturnRequestAsync(db);

        var service = new LockerService(db);
        var request = new ConfirmCompartmentPackageActionRequest("Handover", "Bàn giao lại hàng trả cho người gửi");

        var response = await service.ConfirmCompartmentPackageActionAsync(
            adminUser.Id, nameof(UserRole.Administrator), locker.Id, compartment.Id, request);

        Assert.Equal("Handover", response.Action);

        var updatedReturn = await db.ReturnRequests.SingleAsync(r => r.Id == returnReq.Id);
        Assert.Equal(ReturnRequestStatus.Completed, updatedReturn.Status);
        Assert.NotNull(updatedReturn.CompartmentReleasedAt);

        var updatedReservation = await db.CompartmentReservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.NotNull(updatedReservation.ReleasedAt);
    }

    [Fact]
    public async Task Remove_ReturnRequest_SetsCancelled_AndReleasesCompartmentReleasedAtAndReservation()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, returnReq, reservation) = await SeedLockerWithReturnRequestAsync(db);

        var service = new LockerService(db);
        var request = new ConfirmCompartmentPackageActionRequest("Remove", "Thu hồi hàng trả về văn phòng quản lý");

        var response = await service.ConfirmCompartmentPackageActionAsync(
            adminUser.Id, nameof(UserRole.Administrator), locker.Id, compartment.Id, request);

        Assert.Equal("Remove", response.Action);

        var updatedReturn = await db.ReturnRequests.SingleAsync(r => r.Id == returnReq.Id);
        Assert.Equal(ReturnRequestStatus.Cancelled, updatedReturn.Status);
        Assert.NotNull(updatedReturn.CompartmentReleasedAt);

        var updatedReservation = await db.CompartmentReservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.NotNull(updatedReservation.ReleasedAt);
    }

    [Fact]
    public async Task SetOutOfService_SetsCompartmentOutOfService()
    {
        await using var db = CreateDbContext();
        var (adminUser, locker, compartment, parcel, _) = await SeedLockerWithParcelAsync(db);

        var service = new LockerService(db);
        var request = new ConfirmCompartmentPackageActionRequest("Remove", "Khóa ngăn bảo trì cảm biến", SetOutOfService: true);

        await service.ConfirmCompartmentPackageActionAsync(
            adminUser.Id, nameof(UserRole.Administrator), locker.Id, compartment.Id, request);

        var updatedCompartment = await db.LockerCompartments.SingleAsync(c => c.Id == compartment.Id);
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, updatedCompartment.OperationalStatus);
    }

    [Fact]
    public async Task EmptyCompartment_ThrowsConflictException()
    {
        await using var db = CreateDbContext();
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
        await db.SaveChangesAsync();

        var service = new LockerService(db);
        var request = new ConfirmCompartmentPackageActionRequest("Handover", "Test lý do");

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ConfirmCompartmentPackageActionAsync(user.Id, nameof(UserRole.Administrator), locker.Id, compartment.Id, request));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(User User, Locker Locker, LockerCompartment Compartment, Parcel Parcel, CompartmentReservation Reservation)> SeedLockerWithParcelAsync(ApplicationDbContext db)
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
        return (user, locker, compartment, parcel, reservation);
    }

    private static async Task<(User User, Locker Locker, LockerCompartment Compartment, ReturnRequest ReturnRequest, CompartmentReservation Reservation)> SeedLockerWithReturnRequestAsync(ApplicationDbContext db)
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
            Code = "B02",
            HardwareCode = "HW-02",
            HardwareChannel = 2,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.LockerCompartments.Add(compartment);

        var returnReq = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            AllocatedCompartmentId = compartment.Id,
            ReturnCode = "RET987654",
            Status = ReturnRequestStatus.Deposited,
            AllocatedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.ReturnRequests.Add(returnReq);

        var reservation = new CompartmentReservation
        {
            Id = Guid.NewGuid(),
            LockerCompartmentId = compartment.Id,
            ReturnRequestId = returnReq.Id,
            ReservedAt = Now,
            ExpiresAt = Now.AddDays(3),
            ReleasedAt = null
        };
        db.CompartmentReservations.Add(reservation);

        await db.SaveChangesAsync();
        return (user, locker, compartment, returnReq, reservation);
    }
}
