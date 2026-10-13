using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Maintenance;

public sealed class MaintenanceServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    #region MNT-01: GetAsync
    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var adminId = Guid.NewGuid();
        var locker = CreateLocker("LK-MNT-01");
        var mnt = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Priority = MaintenancePriority.Normal,
            Status = MaintenanceStatus.Open,
            Description = "Hỏng khóa",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, mnt);
        await dbContext.SaveChangesAsync();

        var service = new MaintenanceService(dbContext, TimeProvider.System);
        PagedResult<MaintenanceResponse> result = await service.GetAsync(
            adminId, nameof(UserRole.Administrator), null, null, null, null, null, null, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal(mnt.Id, result.Items[0].Id);
    }

    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        PagedResult<MaintenanceResponse> result = await service.GetAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), Guid.NewGuid(), null, null, null, null, null, default, 1, 10);

        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        PagedResult<MaintenanceResponse> result = await service.GetAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), null, null, null, null, null, null, default, 0, 0);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
    }

    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        PagedResult<MaintenanceResponse> result = await service.GetAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), null, null, null, null, null, null, default, 0, 101);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    [Trait("Category", "MNT-01")]
    public async Task GetAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            Guid.NewGuid(), "Resident", null, null, null, null, null, null, default, 1, 10));
    }
    #endregion

    #region MNT-02: GetByIdAsync
    [Fact]
    [Trait("Category", "MNT-02")]
    public async Task GetByIdAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var adminId = Guid.NewGuid();
        var locker = CreateLocker("LK-MNT-02");
        var mnt = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Priority = MaintenancePriority.High,
            Status = MaintenanceStatus.InProgress,
            Description = "Bảo trì định kỳ",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, mnt);
        await dbContext.SaveChangesAsync();

        var service = new MaintenanceService(dbContext, TimeProvider.System);
        MaintenanceDetailResponse detail = await service.GetByIdAsync(adminId, nameof(UserRole.Administrator), mnt.Id, default);

        Assert.Equal(mnt.Id, detail.Request.Id);
        Assert.Equal("Bảo trì định kỳ", detail.Request.Description);
    }

    [Fact]
    [Trait("Category", "MNT-02")]
    public async Task GetByIdAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid(), nameof(UserRole.Administrator), Guid.NewGuid(), default));
        Assert.Equal("Không tìm thấy yêu cầu bảo trì.", ex.Message);
    }

    [Fact]
    [Trait("Category", "MNT-02")]
    public async Task GetByIdAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetByIdAsync(Guid.NewGuid(), nameof(UserRole.Administrator), Guid.Empty, default));
        Assert.Equal("Không tìm thấy yêu cầu bảo trì.", ex.Message);
    }
    #endregion

    #region MNT-03: CreateAsync
    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var adminId = Guid.NewGuid();
        var locker = CreateLocker("LK-MNT-03");
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = new MaintenanceService(dbContext, TimeProvider.System);
        MaintenanceResponse created = await service.CreateAsync(
            adminId,
            nameof(UserRole.Administrator),
            new CreateMaintenanceRequest(locker.Id, null, null, MaintenancePriority.Low, "Kiểm tra bản lề cửa"),
            default);

        Assert.Equal("Kiểm tra bản lề cửa", created.Description);
        Assert.Single(dbContext.MaintenanceRequests);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Maintenance.Created" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            new CreateMaintenanceRequest(Guid.NewGuid(), null, null, MaintenancePriority.Low, ""),
            default));

        Assert.Equal("Mô tả bảo trì là bắt buộc.", ex.Message);
    }

    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-MNT-03C");
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            new CreateMaintenanceRequest(locker.Id, Guid.NewGuid(), null, MaintenancePriority.Low, "Bảo trì ngăn"),
            default));

        Assert.Equal("Không tìm thấy ngăn tủ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-MNT-03D");
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            LockerId = Guid.NewGuid(), // different locker
            Type = "Locker",
            Title = "Incident khác",
            Description = "Desc",
            Status = IncidentStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, incident);
        await dbContext.SaveChangesAsync();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            new CreateMaintenanceRequest(locker.Id, null, incident.Id, MaintenancePriority.Low, "Bảo trì theo sự cố"),
            default));

        Assert.Equal("Sự cố không thuộc đúng tủ hoặc ngăn được yêu cầu bảo trì.", ex.Message);
    }

    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(
            Guid.Empty, "", null!, default));
    }

    [Fact]
    [Trait("Category", "MNT-03")]
    public async Task CreateAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-MNT-03F");
        dbContext.Add(locker);
        await dbContext.SaveChangesAsync();

        var service = new MaintenanceService(dbContext, TimeProvider.System);
        Guid operatorId = Guid.NewGuid();

        // Operator has no assignment for locker
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            operatorId,
            nameof(UserRole.LockerOperator),
            new CreateMaintenanceRequest(locker.Id, null, null, MaintenancePriority.Low, "Bảo trì cửa"),
            default));

        Assert.Equal("Tủ không thuộc phạm vi vận hành.", ex.Message);
    }
    #endregion

    #region MNT-04: UpdateAsync
    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-MNT-04");
        var mnt = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Priority = MaintenancePriority.Normal,
            Status = MaintenanceStatus.Open,
            Description = "Sửa ngăn",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, mnt);
        await dbContext.SaveChangesAsync();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        MaintenanceResponse response = await service.UpdateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            mnt.Id,
            new UpdateMaintenanceRequest(MaintenanceStatus.Closed, "Đã sửa xong linh kiện", "Khắc phục hoàn tất"),
            default);

        Assert.Equal(MaintenanceStatus.Closed, response.Status);
        Assert.Equal("Khắc phục hoàn tất", response.ResolutionSummary);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Maintenance.StatusChanged" && a.Result == AuditLogResult.Succeeded);
    }

    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            Guid.NewGuid(),
            new UpdateMaintenanceRequest(MaintenanceStatus.InProgress, "Ghi chú", null),
            default));

        Assert.Equal("Không tìm thấy yêu cầu bảo trì.", ex.Message);
    }

    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-MNT-04C");
        var mnt = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Priority = MaintenancePriority.Normal,
            Status = MaintenanceStatus.Open,
            Description = "Sửa ngăn",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, mnt);
        await dbContext.SaveChangesAsync();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            mnt.Id,
            new UpdateMaintenanceRequest(MaintenanceStatus.Closed, "Ghi chú", null),
            default));

        Assert.Equal("Cần nhập kết quả bảo trì khi đóng yêu cầu.", ex.Message);
    }

    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateAsync(
            Guid.Empty, "", Guid.Empty, null!, default));
    }

    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-MNT-04E");
        var mnt = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Priority = MaintenancePriority.Normal,
            Status = MaintenanceStatus.Open,
            Description = "Sửa khóa",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, mnt);
        await dbContext.SaveChangesAsync();
        var service = new MaintenanceService(dbContext, TimeProvider.System);

        MaintenanceResponse response = await service.UpdateAsync(
            Guid.NewGuid(),
            nameof(UserRole.Administrator),
            mnt.Id,
            new UpdateMaintenanceRequest(MaintenanceStatus.InProgress, "Đang sửa", null),
            default);

        Assert.Equal(MaintenanceStatus.InProgress, response.Status);
        Assert.Null(response.ResolutionSummary);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Maintenance.StatusChanged");
    }

    [Fact]
    [Trait("Category", "MNT-04")]
    public async Task UpdateAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var locker = CreateLocker("LK-MNT-04F");
        var mnt = new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Priority = MaintenancePriority.Normal,
            Status = MaintenanceStatus.Open,
            Description = "Sửa khóa",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(locker, mnt);
        await dbContext.SaveChangesAsync();
        var service = new MaintenanceService(dbContext, TimeProvider.System);
        Guid operatorId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(
            operatorId,
            nameof(UserRole.LockerOperator),
            mnt.Id,
            new UpdateMaintenanceRequest(MaintenanceStatus.InProgress, "Đang sửa", null),
            default));

        Assert.Equal("Tủ không thuộc phạm vi vận hành.", ex.Message);
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
}
