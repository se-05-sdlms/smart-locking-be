using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Incidents;

public sealed class IncidentServiceTests
{
    [Fact]
    public async Task CreateResidentIncidentAsync_RoutesToAssignedOperator()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        IncidentDetailResponse result = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest(
                "Retrieval",
                "Không mở được ngăn",
                "Ứng dụng báo thành công nhưng cửa chưa mở.",
                ParcelId: graph.Parcel.Id,
                EvidenceUrl: "https://cdn.boxora.test/evidence.jpg"));

        Assert.Equal(IncidentStatus.Open, result.Status);
        Assert.Equal(graph.Locker.Id, result.LockerId);
        Assert.Equal(graph.Compartment.Id, result.LockerCompartmentId);
        Assert.Equal(graph.Operator.Id, result.AssignedOperatorUserId);
        Assert.Equal("Created", Assert.Single(result.Actions).ActionType);
        Notification notification = await dbContext.Notifications.SingleAsync();
        Assert.Equal(graph.Operator.Id, notification.UserId);
        Assert.Equal(result.Id, notification.IncidentId);
    }

    [Fact]
    public async Task CreateResidentIncidentAsync_RejectsAnotherResidentsParcel()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        User otherResident = CreateUser(UserRole.Resident, "other@boxora.test");
        var otherProfile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = otherResident.Id,
            User = otherResident,
            RegisteredLockerId = graph.Locker.Id,
            FullName = "Other Resident"
        };
        dbContext.AddRange(otherResident, otherProfile);
        await dbContext.SaveChangesAsync();
        var service = new IncidentService(dbContext, TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateResidentIncidentAsync(
            otherResident.Id,
            new CreateIncidentRequest(
                "Parcel",
                "Sai bưu kiện",
                "Không phải bưu kiện của tôi.",
                ParcelId: graph.Parcel.Id)));
    }

    [Fact]
    public async Task UpdateStatusAsync_RecordsTimelineAndNotifiesResident()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest(
                "Compartment",
                "Cửa ngăn bị kẹt",
                "Không thể đóng cửa ngăn.",
                LockerId: graph.Locker.Id,
                LockerCompartmentId: graph.Compartment.Id));

        await service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Investigating, "Đang kiểm tra cảm biến."));
        IncidentDetailResponse resolved = await service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(
                IncidentStatus.Resolved,
                ResolutionSummary: "Đã căn chỉnh lại cảm biến cửa."));

        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal("Đã căn chỉnh lại cảm biến cửa.", resolved.ResolutionSummary);
        Assert.Equal(3, resolved.Actions.Count);
        Assert.Equal(2, await dbContext.Notifications.CountAsync(item => item.UserId == graph.Resident.Id));
    }

    [Fact]
    public async Task GetOperationalIncidentsAsync_RestrictsOperatorToAssignedLocker()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        User unassignedOperator = CreateUser(UserRole.LockerOperator, "other.operator@boxora.test");
        dbContext.Users.Add(unassignedOperator);
        await dbContext.SaveChangesAsync();
        var service = new IncidentService(dbContext, TimeProvider.System);
        await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest(
                "Locker",
                "Màn hình locker không phản hồi",
                "Màn hình bị đứng.",
                LockerId: graph.Locker.Id));

        Assert.Single(await service.GetOperationalIncidentsAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            null));
        Assert.Empty(await service.GetOperationalIncidentsAsync(
            unassignedOperator.Id,
            nameof(UserRole.LockerOperator),
            null));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<TestGraph> SeedGraphAsync(ApplicationDbContext dbContext)
    {
        User administrator = CreateUser(UserRole.Administrator, "admin@boxora.test");
        User resident = CreateUser(UserRole.Resident, "resident@boxora.test");
        User lockerOperator = CreateUser(UserRole.LockerOperator, "operator@boxora.test");
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LOCKER-A",
            Address = "Tòa A",
            RecoveryAddress = "Lễ tân",
            DeviceIdentifier = "device-a"
        };
        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "A01",
            HardwareCode = "A01",
            HardwareChannel = 1
        };
        var profile = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            UserId = resident.Id,
            User = resident,
            RegisteredLockerId = locker.Id,
            RegisteredLocker = locker,
            FullName = "Nguyễn Văn A"
        };
        resident.ResidentProfile = profile;
        var assignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = lockerOperator.Id,
            OperatorUser = lockerOperator,
            LockerId = locker.Id,
            Locker = locker,
            AssignedByUserId = administrator.Id,
            AssignedByUser = administrator,
            AssignedAt = DateTimeOffset.UtcNow
        };
        var deliveryRequest = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = profile.Id,
            ResidentProfile = profile,
            LockerId = locker.Id,
            Locker = locker,
            AllocatedCompartmentId = compartment.Id,
            AllocatedCompartment = compartment
        };
        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryRequest.Id,
            DeliveryRequest = deliveryRequest,
            ParcelCode = "P-001"
        };
        dbContext.AddRange(
            administrator,
            resident,
            lockerOperator,
            locker,
            compartment,
            profile,
            assignment,
            deliveryRequest,
            parcel);
        await dbContext.SaveChangesAsync();
        return new TestGraph(resident, lockerOperator, locker, compartment, parcel);
    }

    private static User CreateUser(UserRole role, string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "hash",
        Role = role,
        Status = UserStatus.Active
    };

    private sealed record TestGraph(
        User Resident,
        User Operator,
        Locker Locker,
        LockerCompartment Compartment,
        Parcel Parcel);
}
