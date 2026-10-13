using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using System.Text.Json;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelServiceTests
{
    [Theory]
    [InlineData(UserRole.Administrator, 4)]
    [InlineData(UserRole.LockerOperator, 2)]
    public async Task ClearancePending_FiltersDeadlineStatusAndAssignment(UserRole role, int expectedCount)
    {
        await using var db = CreateDbContext();
        var owner = CreateUser(UserRole.Resident);
        var requester = CreateUser(role);
        var now = DateTimeOffset.UtcNow;
        foreach (var (status, hours, assigned, revoked) in new[]
        {
            (ParcelStatus.Stored, 0, true, false),
            (ParcelStatus.Overdue, -1, true, false),
            (ParcelStatus.Stored, 1, true, false),
            (ParcelStatus.Retrieved, -1, true, false),
            (ParcelStatus.Removed, -1, true, false),
            (ParcelStatus.Overdue, -1, false, false),
            (ParcelStatus.Overdue, -1, true, true)
        })
        {
            var parcel = AddParcelGraph(db, owner, status, Guid.NewGuid().ToString());
            parcel.MaxStorageUntil = now.AddHours(hours);
            if (assigned)
                db.OperatorAssignments.Add(new OperatorAssignment
                {
                    Id = Guid.NewGuid(),
                    OperatorUserId = requester.Id,
                    LockerId = parcel.DeliveryRequest.LockerId,
                    AssignedByUserId = Guid.NewGuid(),
                    AssignedAt = now.AddDays(-1),
                    RevokedAt = revoked ? now : null
                });
        }
        await db.SaveChangesAsync();
        var result = await new ParcelService(db, new TransferPush(), new FixedClock(now))
            .GetParcelsAsync(requester.Id, role.ToString(), ParcelListView.ClearancePending, null, null, null, default);
        Assert.Equal(expectedCount, result.Count);
        Assert.All(result, parcel =>
        {
            Assert.Contains(parcel.Status, new[] { ParcelStatus.Stored, ParcelStatus.Overdue });
            Assert.True(parcel.MaxStorageUntil <= now);
        });
    }

    [Fact]
    public async Task ClearancePending_ResidentIsForbidden()
    {
        await using var db = CreateDbContext();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ParcelService(db, new TransferPush(), TimeProvider.System)
                .GetParcelsAsync(Guid.NewGuid(), "Resident", ParcelListView.ClearancePending, null, null, null, default));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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

        IReadOnlyCollection<ParcelListItemResponse> result = await new ParcelService(dbContext, new TransferPush(), TimeProvider.System)
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

        IReadOnlyCollection<ParcelListItemResponse> result = await new ParcelService(dbContext, new TransferPush(), TimeProvider.System)
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

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new ParcelService(dbContext, new TransferPush(), TimeProvider.System)
            .GetParcelAsync(requester.Id, nameof(UserRole.Resident), parcel.Id, default));
    }

    [Fact]
    public async Task GetParcelsAsync_WithInvalidDateRange_RejectsRequest()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(() => new ParcelService(dbContext, new TransferPush(), TimeProvider.System).GetParcelsAsync(
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

        IReadOnlyCollection<ParcelStatusHistoryResponse> result = await new ParcelService(dbContext, new TransferPush(), TimeProvider.System)
            .GetHistoryAsync(owner.Id, nameof(UserRole.Resident), parcel.Id, default);

        Assert.Equal(ParcelStatus.Overdue, result.First().ToStatus);
        Assert.Equal(ParcelStatus.Stored, result.Last().ToStatus);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
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
    private sealed class TransferPush : IPushNotificationService
    {
        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode) => Guid.NewGuid();
        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode) => Guid.NewGuid();
        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => Guid.NewGuid();
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => Guid.NewGuid();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => Guid.NewGuid();
        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task TransferOverdue_checks_scope_and_deadline_then_records_removal()
    {
        using var dbContext = CreateDbContext();
        var owner = CreateUser(UserRole.Resident);
        var parcel = AddParcelGraph(dbContext, owner, ParcelStatus.Stored, "TRANSFER");
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddDays(1);
        await dbContext.SaveChangesAsync();
        var service = new ParcelService(dbContext, new TransferPush(), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransferOverdueAsync(Guid.NewGuid(), "Administrator", parcel.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TransferOverdueAsync(Guid.NewGuid(), "LockerOperator", parcel.Id));
        parcel.MaxStorageUntil = DateTimeOffset.UtcNow.AddDays(-1);
        await dbContext.SaveChangesAsync();
        var response = await service.TransferOverdueAsync(Guid.NewGuid(), "Administrator", parcel.Id);
        Assert.Equal(ParcelStatus.Removed, parcel.Status);
        Assert.NotNull(parcel.RemovedAt);
        Assert.Equal(parcel.DeliveryRequest.Locker.RecoveryAddress, response.CollectionAddress);
        Assert.Single(dbContext.AuditLogs);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransferOverdueAsync(Guid.NewGuid(), "Administrator", parcel.Id));
    }

}
