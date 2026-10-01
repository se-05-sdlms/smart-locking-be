using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Parcels;
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

        IReadOnlyCollection<ParcelListItemResponse> result = await new ParcelService(dbContext)
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

        IReadOnlyCollection<ParcelListItemResponse> result = await new ParcelService(dbContext)
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

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new ParcelService(dbContext)
            .GetParcelAsync(requester.Id, nameof(UserRole.Resident), parcel.Id, default));
    }

    [Fact]
    public async Task GetParcelsAsync_WithInvalidDateRange_RejectsRequest()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        User resident = CreateUser(UserRole.Resident);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(() => new ParcelService(dbContext).GetParcelsAsync(
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

        IReadOnlyCollection<ParcelStatusHistoryResponse> result = await new ParcelService(dbContext)
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
}
