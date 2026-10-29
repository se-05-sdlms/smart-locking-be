using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Services;
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
    [Theory]
    [InlineData(false, true, -1, 1)]
    [InlineData(true, false, -1, 1)]
    [InlineData(true, true, -2, -1)]
    [InlineData(true, true, 1, 2)]
    public async Task BluetoothDisabledOrOutsidePolicyWindow_DoesNotIssueCommandOrPermit(bool enabled, bool active, int starts, int ends)
    {
        await using var db = CreateDb(); var now = DateTimeOffset.UtcNow;
        db.SystemPolicies.Add(new SystemPolicy
        {
            Id = Guid.NewGuid(),
            Version = 1,
            Currency = "VND",
            IsActive = active,
            EnableBluetoothUnlock = enabled,
            EffectiveFrom = now.AddHours(starts),
            EffectiveTo = now.AddHours(ends)
        });
        await db.SaveChangesAsync();
        var workflow = new BluetoothWorkflowService(new BluetoothWorkflowStore(db), TimeProvider.System,
            null!, null!, null!, null!, null!);
        var error = await Assert.ThrowsAsync<ConflictException>(() => workflow.PrepareAsync(Guid.NewGuid(), new("R", Guid.NewGuid(), DeviceIdentifier: "LKR-B")));
        Assert.Equal(BluetoothErrorCodes.Disabled, error.ErrorCode);
        await Assert.ThrowsAsync<ConflictException>(() => workflow.PrepareAsync(Guid.NewGuid(), new("N", Guid.NewGuid(), DeviceIdentifier: "LKR-B")));
        Assert.Empty(db.ReturnRequests); Assert.Empty(db.LockerAccessEvents);
    }

    [Fact]
    public async Task ActiveBluetoothPolicy_RejectsRemovedPhoneOfflineWorkflow()
    {
        await using var db = CreateDb(); var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), PhoneNumber = "0900000001", PasswordHash = "x", Role = UserRole.Resident, Status = UserStatus.Active };
        db.AddRange(user, new ResidentProfile { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Resident" },
            new SystemPolicy
            {
                Id = Guid.NewGuid(),
                Version = 1,
                Currency = "VND",
                IsActive = true,
                EnableBluetoothUnlock = true,
                EffectiveFrom = now.AddHours(-1)
            });
        await db.SaveChangesAsync();
        var workflow = new BluetoothWorkflowService(new BluetoothWorkflowStore(db), TimeProvider.System,
            null!, null!, null!, null!, null!);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => workflow.PrepareAsync(user.Id, new("N", Guid.NewGuid(), DeviceIdentifier: "LKR-B")));
        Assert.Equal(BluetoothErrorCodes.InvalidMessage, error.ErrorCode);
        Assert.Empty(db.ReturnRequests);
    }

    [Fact]
    public async Task BluetoothAllocation_UsesDeviceDoorCheckAndStillRespectsReservations()
    {
        await using ApplicationDbContext db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        Locker locker = new() { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B", OperationalStatus = LockerOperationalStatus.Operational };
        LockerCompartment slot = new() { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = "B-01", HardwareCode = "B-01", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Open };
        db.AddRange(locker, slot);
        await db.SaveChangesAsync();
        var service = new CompartmentAllocationService(db, TimeProvider.System);

        Assert.Null(await service.ReserveAvailableAsync(locker.Id, null, Guid.NewGuid(), now.AddMinutes(10)));
        Assert.NotNull(await service.ReserveAvailableAsync(locker.Id, null, Guid.NewGuid(), now.AddMinutes(10), verifyDoorOnDevice: true));
        Assert.Equal(DoorStatus.Open, slot.DoorStatus);
        Assert.Null(await service.ReserveAvailableAsync(locker.Id, null, Guid.NewGuid(), now.AddMinutes(10), verifyDoorOnDevice: true));
    }

    [Fact]
    public async Task BluetoothAllocation_DoesNotReuseCompartmentWithStoredReturn()
    {
        await using ApplicationDbContext db = CreateDb();
        var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B", OperationalStatus = LockerOperationalStatus.Operational };
        var slot = new LockerCompartment { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = "B-01", HardwareCode = "B-01", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
        db.AddRange(locker, slot, new ReturnRequest { Id = Guid.NewGuid(), LockerId = locker.Id, AllocatedCompartmentId = slot.Id, ReturnCode = "123456", Status = ReturnRequestStatus.Deposited });
        await db.SaveChangesAsync();

        var service = new CompartmentAllocationService(db, TimeProvider.System);
        Assert.Null(await service.ReserveAvailableAsync(locker.Id, null, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), verifyDoorOnDevice: true));
    }

    [Fact]
    public async Task CreateAsync_UsesConnectedLockerInsteadOfRegisteredLocker()
    {
        await using ApplicationDbContext db = CreateDb();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new() { Id = Guid.NewGuid(), PhoneNumber = "0900000001", PasswordHash = "x", Role = UserRole.Resident, Status = UserStatus.Active };
        Locker registered = new() { Id = Guid.NewGuid(), Code = "A", DeviceIdentifier = "LKR-A", Address = "A", RecoveryAddress = "A", OperationalStatus = LockerOperationalStatus.Operational };
        Locker connected = new() { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B", OperationalStatus = LockerOperationalStatus.Operational };
        ResidentProfile resident = new() { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Test", RegisteredLockerId = registered.Id, RegisteredLocker = registered };
        db.AddRange(user, registered, connected, resident);
        await db.SaveChangesAsync();
        var service = new ReturnRequestService(db, new SuccessfulAccess(), new CompartmentAllocationService(db, TimeProvider.System), new NoopPush(), new Sha256TokenHashService(), TimeProvider.System);

        ReturnRequestResponse response = await service.CreateAsync(user.Id, new("https://example.com/image.jpg", null, "LKR-B"));

        Assert.Equal("B", response.LockerCode);
        Assert.Equal(connected.Id, Assert.Single(db.ReturnRequests).LockerId);
        Assert.Equal(registered.Id, resident.RegisteredLockerId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullReturnFlow_UsesSixDigitCodeAndCompletesOnce(bool cancellationRequested)
    {
        await using ApplicationDbContext db = CreateDb();
        Guid userId = Guid.NewGuid(); Guid lockerId = Guid.NewGuid(); Guid compartmentId = Guid.NewGuid(); DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new() { Id = userId, PhoneNumber = "0900000001", PasswordHash = "x", Role = UserRole.Resident, Status = UserStatus.Active, CreatedAt = now, UpdatedAt = now };
        Locker locker = new() { Id = lockerId, Code = "LK-01", Address = "A", RecoveryAddress = "B", DeviceIdentifier = "SIM", OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = now, UpdatedAt = now };
        ResidentProfile resident = new() { Id = Guid.NewGuid(), UserId = userId, User = user, RegisteredLockerId = lockerId, RegisteredLocker = locker, FullName = "Test", DeliveryApprovalMode = DeliveryApprovalMode.Manual, CreatedAt = now, UpdatedAt = now };
        LockerCompartment compartment = new() { Id = compartmentId, LockerId = lockerId, Locker = locker, Code = "A01", HardwareCode = "A01", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed, CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, locker, resident, compartment, new SystemPolicy { Id = Guid.NewGuid(), Version = 1, IsActive = true, GuestSessionTimeoutMinutes = 10, CompartmentReservationMinutes = 10, Currency = "VND", EffectiveFrom = now, CreatedAt = now, CreatedByUserId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var service = new ReturnRequestService(db, new SuccessfulAccess(), new CompartmentAllocationService(db, TimeProvider.System), new NoopPush(), new Sha256TokenHashService(), TimeProvider.System);

        ReturnRequestResponse created = await service.CreateAsync(userId, new CreateReturnRequest("https://example.com/return.jpg", null));
        ReturnUnlockResponse opened = await service.OpenCompartmentAsync(userId, created.Id);
        await service.FinalizeDepositAsync(created.Id, now);
        ReturnRequestResponse deposited = await service.GetAsync(userId, created.Id);
        ReturnPickupSessionResponse session = await service.CreateAsync(new ValidateReturnPickupRequest("LK-01", deposited.PickupCode));
        await service.OpenCompartmentAsync(created.Id, session.GuestSessionToken);
        if (cancellationRequested)
        {
            var cancelled = await service.CancelAsync(userId, created.Id);
            Assert.Equal("PendingVerification", cancelled.Cancellation!.Status);
            Assert.Empty(cancelled.PickupCode);
            Assert.Null((await db.ReturnRequests.SingleAsync()).CompartmentReleasedAt);
            await Assert.ThrowsAsync<ConflictException>(() => service.OpenCompartmentAsync(created.Id, session.GuestSessionToken,
                accessMethod: LockerAccessMethod.Bluetooth));
        }
        await service.FinalizePickupAsync(created.Id, now);
        ReturnRequestResponse completed = await service.GetAsync(userId, created.Id);

        Assert.Matches("^[0-9]{6}$", deposited.PickupCode);
        Assert.Equal("A01", opened.CompartmentCode);
        Assert.Equal(ReturnRequestStatus.Completed, completed.Status);
        Assert.Empty(completed.PickupCode);
        if (cancellationRequested) Assert.Equal("AlreadyCollected", completed.Cancellation!.Status);
        else Assert.True((await service.GetAsync(created.Id, session.GuestSessionToken)).Completed);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(new ValidateReturnPickupRequest("LK-01", deposited.PickupCode)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BluetoothAllocation_RespectsIndependentOperationalBlocks(bool incident)
    {
        await using var db = CreateDb();
        var locker = new Locker { Id = Guid.NewGuid(), Code = "B", DeviceIdentifier = "LKR-B", Address = "B", RecoveryAddress = "B", OperationalStatus = LockerOperationalStatus.Operational };
        var slot = new LockerCompartment { Id = Guid.NewGuid(), Locker = locker, LockerId = locker.Id, Code = "B-01", HardwareCode = "CH1", HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
        db.AddRange(locker, slot);
        if (incident) db.Add(new Incident { Id = Guid.NewGuid(), LockerId = locker.Id, LockerCompartmentId = slot.Id, Type = "compartment", Status = IncidentStatus.Open });
        else db.Add(new MaintenanceRequest { Id = Guid.NewGuid(), LockerId = locker.Id, LockerCompartmentId = slot.Id, Status = MaintenanceStatus.Open });
        await db.SaveChangesAsync();
        Assert.Null(await new CompartmentAllocationService(db, TimeProvider.System).ReserveAvailableAsync(locker.Id, null, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), verifyDoorOnDevice: true));
        Assert.Empty(db.CompartmentReservations);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class SuccessfulAccess : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId, "SIM", 1, LockerAccessResult.Succeeded, null, DateTimeOffset.UtcNow));
    }
    private sealed class NoopPush : IPushNotificationService
    {
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => Guid.NewGuid();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => Guid.NewGuid();
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => Guid.NewGuid();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => Guid.NewGuid();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => Guid.NewGuid();
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
