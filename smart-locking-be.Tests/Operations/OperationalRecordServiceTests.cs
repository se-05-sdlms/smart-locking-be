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
            Guid.NewGuid(), nameof(UserRole.Administrator), "P-OPR", locker.Id, "Parcel", null, null, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal("Parcel", result.Items[0].Kind);
        Assert.Equal("P-OPR-01", result.Items[0].Code);
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-02");
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = new OperationalRecordService(dbContext);
        var operatorId = Guid.NewGuid();

        // Operator has no assignment for locker
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync(
            operatorId, nameof(UserRole.LockerOperator), null, locker.Id, null, null, null, default, 1, 10));

        Assert.Equal("Tủ không thuộc phạm vi vận hành.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OPR-01")]
    public async Task SearchAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new OperationalRecordService(dbContext);

        PagedResult<OperationalRecordResponse> result = await service.SearchAsync(
            Guid.Empty, nameof(UserRole.Administrator), null, null, null, null, null, default, 0, 0);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
    }
    #endregion

    #region OPR-02: GetDetailAsync
    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-OPR-02A");
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
        var service = new OperationalRecordService(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "unsupported_kind", Guid.NewGuid(), default));

        Assert.StartsWith("Operational record kind is invalid.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new OperationalRecordService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetDetailAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), "parcel", Guid.NewGuid(), default));

        Assert.Equal("Operational record not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "OPR-02")]
    public async Task GetDetailAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new OperationalRecordService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetDetailAsync(
            Guid.Empty, "", "", Guid.Empty, default));
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
