using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using System.Text.Json;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelServiceTests
{
    [Fact]
    public void ParcelResponse_UsesStableStringEnums()
    {
        var response = new ParcelStatusHistoryResponse(
            Guid.NewGuid(),
            ParcelStatus.Stored,
            ParcelStatus.Overdue,
            null,
            DateTimeOffset.UtcNow);

        string json = JsonSerializer.Serialize(response);

        Assert.Contains("\"FromStatus\":\"Stored\"", json);
        Assert.Contains("\"ToStatus\":\"Overdue\"", json);
    }

    [Fact]
    public async Task GetParcelsAsync_ForResident_ReturnsOnlyOwnedActiveParcels()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        User other = CreateUser(UserRole.Resident);
        Parcel active = AddParcelGraph(dbContext, owner, ParcelStatus.Stored, "P-OWNED");
        AddParcelGraph(dbContext, owner, ParcelStatus.Retrieved, "P-HISTORY");
        AddParcelGraph(dbContext, other, ParcelStatus.Overdue, "P-OTHER");
        await dbContext.SaveChangesAsync();

        IReadOnlyCollection<ParcelListItemResponse> result = await CreateService(dbContext)
            .GetParcelsAsync(owner.Id, nameof(UserRole.Resident), ParcelListView.Active, null, null, null, default);

        ParcelListItemResponse parcel = Assert.Single(result);
        Assert.Equal(active.Id, parcel.Id);
        Assert.Equal("P-OWNED", parcel.ParcelCode);
    }

    [Fact]
    public async Task GetParcelsAsync_ForOperator_ReturnsOnlyAssignedLockerParcels()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        User operatorUser = CreateUser(UserRole.LockerOperator);
        Parcel assigned = AddParcelGraph(dbContext, resident, ParcelStatus.Overdue, "P-ASSIGNED");
        AddParcelGraph(dbContext, resident, ParcelStatus.Stored, "P-UNASSIGNED");
        dbContext.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorUser.Id,
            LockerId = assigned.DeliveryRequest.LockerId,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        IReadOnlyCollection<ParcelListItemResponse> result = await CreateService(dbContext)
            .GetParcelsAsync(operatorUser.Id, nameof(UserRole.LockerOperator), ParcelListView.All, null, null, null, default);

        Assert.Equal(assigned.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task GetParcelAsync_WhenParcelBelongsToAnotherResident_ReturnsNotFound()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        User requester = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(dbContext, owner, ParcelStatus.Stored, "P-PRIVATE");
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(dbContext)
            .GetParcelAsync(requester.Id, nameof(UserRole.Resident), parcel.Id, default));
    }

    [Fact]
    public async Task GetParcelsAsync_WithInvalidDateRange_RejectsRequest()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(dbContext).GetParcelsAsync(
            resident.Id,
            nameof(UserRole.Resident),
            ParcelListView.History,
            null,
            now,
            now.AddDays(-1),
            default));
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsNewestEventFirst()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User owner = CreateUser(UserRole.Resident);
        Parcel parcel = AddParcelGraph(dbContext, owner, ParcelStatus.Overdue, "P-HISTORY");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        dbContext.ParcelStatusHistories.AddRange(
            new ParcelStatusHistory
            {
                Id = Guid.NewGuid(),
                ParcelId = parcel.Id,
                ToStatus = ParcelStatus.Stored,
                ChangedAt = now.AddHours(-2)
            },
            new ParcelStatusHistory
            {
                Id = Guid.NewGuid(),
                ParcelId = parcel.Id,
                FromStatus = ParcelStatus.Stored,
                ToStatus = ParcelStatus.Overdue,
                ChangedAt = now
            });
        await dbContext.SaveChangesAsync();

        IReadOnlyCollection<ParcelStatusHistoryResponse> result = await CreateService(dbContext)
            .GetHistoryAsync(owner.Id, nameof(UserRole.Resident), parcel.Id, default);

        Assert.Equal(ParcelStatus.Overdue, result.First().ToStatus);
        Assert.Equal(ParcelStatus.Stored, result.Last().ToStatus);
    }

    [Fact]
    public async Task TransferOverdueAsync_WhenEligible_RemovesParcelAndNotifiesResident()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        User administrator = CreateUser(UserRole.Administrator);
        Parcel parcel = AddParcelGraph(dbContext, resident, ParcelStatus.Overdue, "P-OVERDUE");
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        dbContext.Users.Add(administrator);
        await dbContext.SaveChangesAsync();
        var pushNotifications = new RecordingPushNotificationService();
        var service = new ParcelService(
            dbContext,
            new UnusedLockerAccessService(),
            pushNotifications,
            TimeProvider.System);

        OverdueTransferResponse response = await service.TransferOverdueAsync(
            administrator.Id,
            nameof(UserRole.Administrator),
            parcel.Id);

        Assert.Equal(ParcelStatus.Removed, parcel.Status);
        Assert.Equal("456 Nguyen Van Linh", response.CollectionAddress);
        Assert.Equal(resident.Id, pushNotifications.ResidentUserId);
        Assert.True(pushNotifications.WasSent);
        Assert.Contains(dbContext.AuditLogs, log =>
            log.EntityId == parcel.Id && log.Action == "Parcel.Transferred");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ParcelService CreateService(ApplicationDbContext dbContext) =>
        new(dbContext, new UnusedLockerAccessService(), new UnusedPushNotificationService(), TimeProvider.System);

    private sealed class UnusedLockerAccessService : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedPushNotificationService : IPushNotificationService
    {
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => throw new NotSupportedException();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => throw new NotSupportedException();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => throw new NotSupportedException();
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => throw new NotSupportedException();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => throw new NotSupportedException();
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingPushNotificationService : IPushNotificationService
    {
        private readonly Guid notificationId = Guid.NewGuid();

        public Guid? ResidentUserId { get; private set; }
        public bool WasSent { get; private set; }

        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress)
        {
            ResidentUserId = residentUserId;
            return notificationId;
        }

        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => notificationId;

        public Task TrySendAsync(Guid id, CancellationToken cancellationToken = default)
        {
            WasSent = id == notificationId;
            return Task.CompletedTask;
        }

        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => throw new NotSupportedException();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => throw new NotSupportedException();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => throw new NotSupportedException();
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static User CreateUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
        PasswordHash = "hash",
        Role = role,
        Status = UserStatus.Active
    };

    private static Parcel AddParcelGraph(
        ApplicationDbContext dbContext,
        User residentUser,
        ParcelStatus status,
        string parcelCode)
    {
        ResidentProfile? existingProfile = dbContext.ResidentProfiles.Local
            .SingleOrDefault(candidate => candidate.UserId == residentUser.Id);
        var profile = existingProfile ?? new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = residentUser.Id,
            FullName = "Resident",
        };
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = $"L-{Guid.NewGuid():N}"[..10],
            Address = "123 Nguyen Van Linh",
            RecoveryAddress = "456 Nguyen Van Linh",
            DeviceIdentifier = Guid.NewGuid().ToString(),
            OperationalStatus = LockerOperationalStatus.Operational
        };
        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Code = "A01",
            HardwareCode = Guid.NewGuid().ToString(),
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational
        };
        var deliveryRequest = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = profile.Id,
            LockerId = locker.Id,
            AllocatedCompartmentId = compartment.Id,
            SystemPolicyId = Guid.NewGuid(),
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.Deposited
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryRequest.Id,
            ParcelCode = parcelCode,
            Status = status,
            StoredAt = now.AddHours(-1),
            PickupDueAt = now.AddHours(23),
            MaxStorageUntil = now.AddDays(3),
            RetrievedAt = status == ParcelStatus.Retrieved ? now : null,
            DeliveryRequest = deliveryRequest
        };

        if (existingProfile is null)
        {
            dbContext.AddRange(residentUser, profile);
        }
        dbContext.AddRange(locker, compartment, deliveryRequest, parcel);
        return parcel;
    }
}
