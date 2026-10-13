using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Operations;

public sealed class OperationalRecordServiceTests
{
    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    #region OPR-01: SearchAsync
    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-01");
        var parcel = CreateParcel(locker, "P-OPR-01");
        dbContext.AddRange(locker, parcel.DeliveryRequest, parcel);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        PagedResult<OperationalRecordResponse> result = await service.SearchAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), null, null, null, null, null, default, 1, 20);

        Assert.NotEmpty(result.Items);
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker1 = CreateLocker("LK-OPR-02A");
        var locker2 = CreateLocker("LK-OPR-02B");
        var parcel1 = CreateParcel(locker1, "P-OPR-02A");
        var parcel2 = CreateParcel(locker2, "P-OPR-02B");
        Guid operatorId = Guid.NewGuid();
        var assignment = new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorId,
            LockerId = locker1.Id,
            AssignedByUserId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker1, locker2, parcel1.DeliveryRequest, parcel1, parcel2.DeliveryRequest, parcel2, assignment);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        PagedResult<OperationalRecordResponse> result = await service.SearchAsync(
            operatorId, nameof(UserRole.LockerOperator), null, null, null, null, null, default, 1, 20);

        Assert.All(result.Items, item => Assert.Equal(locker1.Code, item.LockerCode));
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-03");
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        var operatorId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync(
            operatorId, nameof(UserRole.LockerOperator), null, locker.Id, null, null, null, default, 1, 20));

        Assert.Equal("Tủ không thuộc phạm vi vận hành.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-04");
        var parcel = CreateParcel(locker, "ABC-123");
        dbContext.AddRange(locker, parcel.DeliveryRequest, parcel);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        PagedResult<OperationalRecordResponse> result = await service.SearchAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "ABC", null, null, null, null, default, 1, 20);

        Assert.Single(result.Items);
        Assert.Equal("ABC-123", result.Items[0].Code);
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-05");
        var parcel = CreateParcel(locker, "P-OPR-05");
        dbContext.AddRange(locker, parcel.DeliveryRequest, parcel);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        PagedResult<OperationalRecordResponse> result = await service.SearchAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), null, null, "Parcel", null, null, default, 1, 20);

        Assert.All(result.Items, item => Assert.Equal("Parcel", item.Kind));
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-06");
        DateTimeOffset from = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        locker.UpdatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        PagedResult<OperationalRecordResponse> result = await service.SearchAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), null, null, null, from, to, default, 1, 20);

        Assert.All(result.Items, item =>
        {
            Assert.True(item.OccurredAt >= from);
            Assert.True(item.OccurredAt < to);
        });
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new OperationalRecordService(dbContext);

        PagedResult<OperationalRecordResponse> result = await service.SearchAsync(
            Guid.Empty, nameof(UserRole.Administrator), null, null, null, null, null, default, 0, 0);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new OperationalRecordService(dbContext);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync(
            Guid.NewGuid(), "Resident", null, null, null, null, null, default, 1, 20));
    }
    #endregion

    #region OPR-02: GetDetailAsync
    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-D01");
        var parcel = CreateParcel(locker, "P-DETAIL-01");
        dbContext.AddRange(locker, parcel.DeliveryRequest, parcel);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        OperationalRecordDetailResponse response = await service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "parcel", parcel.Id, default);

        Assert.Equal("Parcel", response.Kind);
        Assert.Equal(parcel.Id, response.Id);
        Assert.Equal("P-DETAIL-01", response.Code);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-D02");
        var returnReq = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            ReturnCode = "RET-01",
            ResidentProfileId = Guid.NewGuid(),
            Status = ReturnRequestStatus.Created,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, returnReq);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        OperationalRecordDetailResponse response = await service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "return-request", returnReq.Id, default);

        Assert.Equal("ReturnRequest", response.Kind);
        Assert.Equal("RET-01", response.Code);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-D03");
        var delivery = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.Deposited,
            LastActivityAt = DateTimeOffset.UtcNow,
            SessionExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, delivery);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        OperationalRecordDetailResponse response = await service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "delivery-request", delivery.Id, default);

        Assert.Equal("DeliveryRequest", response.Kind);
        Assert.Equal(delivery.Id, response.Id);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-D04");
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Title = "Incident 01",
            Description = "Incident desc",
            Type = "HardwareFailure",
            Status = IncidentStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, incident);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        OperationalRecordDetailResponse response = await service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "incident", incident.Id, default);

        Assert.Equal("Incident", response.Kind);
        Assert.Equal(incident.Id, response.Id);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-D05");
        var maintenance = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            CreatedByUserId = Guid.NewGuid(),
            Description = "Fix locker door",
            Priority = MaintenancePriority.High,
            Status = MaintenanceStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, maintenance);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        OperationalRecordDetailResponse response = await service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "maintenance-request", maintenance.Id, default);

        Assert.Equal("MaintenanceRequest", response.Kind);
        Assert.Equal(maintenance.Id, response.Id);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-D06");
        var lockerEvent = new LockerEvent
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            EventType = LockerEventType.DoorChanged,
            Severity = LockerEventSeverity.Info,
            Details = "Door 1 opened",
            OccurredAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, lockerEvent);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        OperationalRecordDetailResponse response = await service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "locker-event", lockerEvent.Id, default);

        Assert.Equal("LockerEvent", response.Kind);
        Assert.Equal(lockerEvent.Id, response.Id);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new OperationalRecordService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "unknown", Guid.NewGuid(), default));

        Assert.StartsWith("Operational record kind is invalid.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new OperationalRecordService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "parcel", Guid.NewGuid(), default));

        Assert.Equal("Operational record not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC09()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-D09");
        var parcel = CreateParcel(locker, "P-D09");
        dbContext.AddRange(locker, parcel.DeliveryRequest, parcel);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        Guid operatorId = Guid.NewGuid();

        // Operator has no scope on this locker
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetDetailAsync(
            operatorId, nameof(UserRole.LockerOperator), "parcel", parcel.Id, default));

        Assert.Equal("Operational record not found.", ex.Message);
    }
    #endregion

    private static Locker CreateLocker(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Address = "Address",
        RecoveryAddress = "Recovery",
        DeviceIdentifier = $"DEV-{code}",
        OperationalStatus = LockerOperationalStatus.Operational,
        ConnectionStatus = LockerConnectionStatus.Online,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static Parcel CreateParcel(Locker locker, string code)
    {
        var delivery = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            GuestSessionTokenHash = "hash",
            Status = DeliveryRequestStatus.Deposited,
            LastActivityAt = DateTimeOffset.UtcNow,
            SessionExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = delivery.Id,
            DeliveryRequest = delivery,
            ParcelCode = code,
            Status = ParcelStatus.Stored,
            StoredAt = DateTimeOffset.UtcNow,
            PickupDueAt = DateTimeOffset.UtcNow.AddDays(2),
            MaxStorageUntil = DateTimeOffset.UtcNow.AddDays(5),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
