using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Returns;

public sealed class ReturnRequestServiceTests
{
    [Fact]
    public async Task FullReturnFlow_UsesSixDigitCodeAndCompletesOnce()
    {
        await using ApplicationDbContext db = CreateDb();
        Guid userId = Guid.NewGuid(); Guid lockerId = Guid.NewGuid(); Guid compartmentId = Guid.NewGuid(); DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new() { Id = userId, PhoneNumber = "0900000001", PasswordHash = "x", Role = UserRole.Resident, Status = UserStatus.Active, CreatedAt = now, UpdatedAt = now };
        Locker locker = new() { Id = lockerId, Code = "LK-01", Address = "A", RecoveryAddress = "B", DeviceIdentifier = "SIM", OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = now, UpdatedAt = now };
        ResidentProfile resident = new() { Id = Guid.NewGuid(), UserId = userId, User = user, RegisteredLockerId = lockerId, RegisteredLocker = locker, FullName = "Test", DeliveryApprovalMode = DeliveryApprovalMode.Manual, CreatedAt = now, UpdatedAt = now };
        LockerCompartment compartment = new() { Id = compartmentId, LockerId = lockerId, Locker = locker, Code = "A01", HardwareCode = "A01", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed, CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, locker, resident, compartment, new SystemPolicy { Id = Guid.NewGuid(), Version = 1, IsActive = true, GuestSessionTimeoutMinutes = 10, CompartmentReservationMinutes = 10, Currency = "VND", EffectiveFrom = now, CreatedAt = now, CreatedByUserId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var service = new ReturnRequestService(db, new SuccessfulAccess(), new NoopPush(), new Sha256TokenHashService(), TimeProvider.System);

        ReturnRequestResponse created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        ReturnUnlockResponse opened = await service.AllocateAndOpenAsync(userId, created.Id);
        ReturnDepositResponse deposited = await service.ConfirmDepositAsync(userId, created.Id);
        ReturnPickupSessionResponse session = await service.ValidatePickupAsync(new ValidateReturnPickupRequest("LK-01", deposited.PickupCode));
        await service.OpenForPickupAsync(created.Id, session.GuestSessionToken);
        ReturnPickupCompleteResponse completed = await service.ConfirmPickupAsync(created.Id, session.GuestSessionToken);

        Assert.Matches("^[0-9]{6}$", deposited.PickupCode);
        Assert.Equal("A01", opened.CompartmentCode);
        Assert.Equal(ReturnRequestStatus.Completed, completed.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidatePickupAsync(new ValidateReturnPickupRequest("LK-01", deposited.PickupCode)));
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class SuccessfulAccess : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId, "SIM", 1, LockerAccessResult.Succeeded, null, DateTimeOffset.UtcNow));
    }
    private sealed class NoopPush : IPushNotificationService
    {
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => Guid.NewGuid();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => Guid.NewGuid();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => Guid.NewGuid();
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
