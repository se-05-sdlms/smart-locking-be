using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class EmergencyUnlockTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("123")]
    [InlineData("1234")]
    public async Task EmergencyUnlock_ReasonTooShort_ThrowsArgumentException(string reason)
    {
        await using var db = CreateDbContext();
        var service = new LockerService(db, new FakeLockerAccessService(), new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest(reason);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EmergencyUnlockCompartmentAsync(
                Guid.NewGuid(),
                nameof(UserRole.Administrator),
                Guid.NewGuid(),
                Guid.NewGuid(),
                request,
                "127.0.0.1"));
    }

    [Fact]
    public async Task EmergencyUnlock_NonExistentLocker_ThrowsKeyNotFoundException()
    {
        await using var db = CreateDbContext();
        var service = new LockerService(db, new FakeLockerAccessService(), new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest("Lý do hợp lệ để mở khẩn cấp");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.EmergencyUnlockCompartmentAsync(
                Guid.NewGuid(),
                nameof(UserRole.Administrator),
                Guid.NewGuid(),
                Guid.NewGuid(),
                request,
                "127.0.0.1"));
    }

    [Fact]
    public async Task EmergencyUnlock_NonExistentCompartment_ThrowsKeyNotFoundException()
    {
        await using var db = CreateDbContext();
        var (locker, _) = await SeedLockerAsync(db, LockerConnectionStatus.Online);
        var service = new LockerService(db, new FakeLockerAccessService(), new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest("Lý do hợp lệ để mở khẩn cấp");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.EmergencyUnlockCompartmentAsync(
                Guid.NewGuid(),
                nameof(UserRole.Administrator),
                locker.Id,
                Guid.NewGuid(),
                request,
                "127.0.0.1"));
    }

    [Fact]
    public async Task EmergencyUnlock_UnauthorizedRole_ThrowsUnauthorizedAccessException()
    {
        await using var db = CreateDbContext();
        var (locker, compartment) = await SeedLockerAsync(db, LockerConnectionStatus.Online);
        var service = new LockerService(db, new FakeLockerAccessService(), new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest("Lý do hợp lệ để mở khẩn cấp");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EmergencyUnlockCompartmentAsync(
                Guid.NewGuid(),
                nameof(UserRole.Resident),
                locker.Id,
                compartment.Id,
                request,
                "127.0.0.1"));
    }

    [Fact]
    public async Task EmergencyUnlock_OperatorNotAssigned_ThrowsUnauthorizedAccessException()
    {
        await using var db = CreateDbContext();
        var (locker, compartment) = await SeedLockerAsync(db, LockerConnectionStatus.Online);
        var service = new LockerService(db, new FakeLockerAccessService(), new FixedTimeProvider(TestNow));

        var unassignedOperatorId = Guid.NewGuid();
        var request = new EmergencyUnlockRequest("Lý do hợp lệ để mở khẩn cấp");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EmergencyUnlockCompartmentAsync(
                unassignedOperatorId,
                nameof(UserRole.LockerOperator),
                locker.Id,
                compartment.Id,
                request,
                "127.0.0.1"));
    }

    [Fact]
    public async Task EmergencyUnlock_OfflineLocker_ThrowsInvalidOperationException()
    {
        await using var db = CreateDbContext();
        var (locker, compartment) = await SeedLockerAsync(db, LockerConnectionStatus.Offline);
        var service = new LockerService(db, new FakeLockerAccessService(), new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest("Lý do hợp lệ để mở khẩn cấp");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EmergencyUnlockCompartmentAsync(
                Guid.NewGuid(),
                nameof(UserRole.Administrator),
                locker.Id,
                compartment.Id,
                request,
                "127.0.0.1"));

        Assert.Contains("ngoại tuyến", ex.Message);
    }

    [Fact]
    public async Task EmergencyUnlock_AssignedOperator_SucceedsAndPersistsRecords()
    {
        await using var db = CreateDbContext();
        var (locker, compartment) = await SeedLockerAsync(db, LockerConnectionStatus.Online);
        var operatorId = Guid.NewGuid();

        // Thêm phân công operator
        db.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorId,
            LockerId = locker.Id,
            AssignedAt = TestNow,
            RevokedAt = null
        });
        await db.SaveChangesAsync();

        var fakeAccessService = new FakeLockerAccessService
        {
            ResponseResult = LockerAccessResult.Succeeded
        };
        var service = new LockerService(db, fakeAccessService, new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest("Xử lý kẹt hàng ngăn số 1");

        var response = await service.EmergencyUnlockCompartmentAsync(
            operatorId,
            nameof(UserRole.LockerOperator),
            locker.Id,
            compartment.Id,
            request,
            "192.168.1.100");

        Assert.NotNull(response);
        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);
        Assert.Equal(locker.Id, response.LockerId);
        Assert.Equal(compartment.Id, response.LockerCompartmentId);
        Assert.Null(response.FailureReason);

        // Kiểm tra bản ghi EmergencyUnlock trong DB
        var persistedUnlock = await db.EmergencyUnlocks.SingleOrDefaultAsync();
        Assert.NotNull(persistedUnlock);
        Assert.Equal(operatorId, persistedUnlock.OperatorUserId);
        Assert.Equal(locker.Id, persistedUnlock.LockerId);
        Assert.Equal(compartment.Id, persistedUnlock.LockerCompartmentId);
        Assert.Equal("Xử lý kẹt hàng ngăn số 1", persistedUnlock.Reason);
        Assert.Equal(EmergencyUnlockResult.Succeeded, persistedUnlock.Result);

        // Kiểm tra bản ghi AuditLog trong DB
        var persistedAudit = await db.AuditLogs.SingleOrDefaultAsync();
        Assert.NotNull(persistedAudit);
        Assert.Equal(operatorId, persistedAudit.ActorUserId);
        Assert.Equal("EmergencyUnlock", persistedAudit.Action);
        Assert.Equal("LockerCompartment", persistedAudit.EntityType);
        Assert.Equal(compartment.Id, persistedAudit.EntityId);
        Assert.Equal(AuditLogResult.Succeeded, persistedAudit.Result);
        Assert.Equal("192.168.1.100", persistedAudit.IpAddress);
        Assert.Contains("Xử lý kẹt hàng ngăn số 1", persistedAudit.Details);
    }

    [Fact]
    public async Task EmergencyUnlock_Administrator_SucceedsWithoutAssignment()
    {
        await using var db = CreateDbContext();
        var (locker, compartment) = await SeedLockerAsync(db, LockerConnectionStatus.Online);
        var adminId = Guid.NewGuid();

        var fakeAccessService = new FakeLockerAccessService
        {
            ResponseResult = LockerAccessResult.Succeeded
        };
        var service = new LockerService(db, fakeAccessService, new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest("Kiểm tra kỹ thuật định kỳ hệ thống khóa");

        var response = await service.EmergencyUnlockCompartmentAsync(
            adminId,
            nameof(UserRole.Administrator),
            locker.Id,
            compartment.Id,
            request,
            "10.0.0.1");

        Assert.NotNull(response);
        Assert.Equal(EmergencyUnlockResult.Succeeded, response.Result);
        Assert.Single(db.EmergencyUnlocks);
        Assert.Single(db.AuditLogs);
    }

    [Fact]
    public async Task EmergencyUnlock_DispatchFailure_RecordsFailedUnlockAndAuditLog()
    {
        await using var db = CreateDbContext();
        var (locker, compartment) = await SeedLockerAsync(db, LockerConnectionStatus.Online);
        var adminId = Guid.NewGuid();

        var fakeAccessService = new FakeLockerAccessService
        {
            ResponseResult = LockerAccessResult.Failed,
            FailureReason = "Mất kết nối MQTT tới rơ-le"
        };
        var service = new LockerService(db, fakeAccessService, new FixedTimeProvider(TestNow));

        var request = new EmergencyUnlockRequest("Mở khóa kiểm tra bảo trì");

        var response = await service.EmergencyUnlockCompartmentAsync(
            adminId,
            nameof(UserRole.Administrator),
            locker.Id,
            compartment.Id,
            request,
            "10.0.0.1");

        Assert.NotNull(response);
        Assert.Equal(EmergencyUnlockResult.Failed, response.Result);
        Assert.Equal("Mất kết nối MQTT tới rơ-le", response.FailureReason);

        var unlock = await db.EmergencyUnlocks.SingleOrDefaultAsync();
        Assert.NotNull(unlock);
        Assert.Equal(EmergencyUnlockResult.Failed, unlock.Result);

        var audit = await db.AuditLogs.SingleOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal(AuditLogResult.Failed, audit.Result);
    }

    private static async Task<(Locker Locker, LockerCompartment Compartment)> SeedLockerAsync(
        ApplicationDbContext dbContext,
        LockerConnectionStatus connectionStatus)
    {
        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LCK-TEST-01",
            Address = "Tòa nhà Innovation",
            RecoveryAddress = "Kho trung tâm",
            DeviceIdentifier = "LKR-TEST01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = connectionStatus,
            CreatedAt = TestNow,
            UpdatedAt = TestNow,
        };

        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Locker = locker,
            Code = "N-01",
            HardwareCode = "RELAY-01",
            HardwareChannel = 1,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = TestNow,
            UpdatedAt = TestNow,
        };

        dbContext.AddRange(locker, compartment);
        await dbContext.SaveChangesAsync();
        return (locker, compartment);
    }

    private sealed class FakeLockerAccessService : ILockerAccessService
    {
        public LockerAccessResult ResponseResult { get; set; } = LockerAccessResult.Succeeded;
        public string? FailureReason { get; set; }

        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OpenLockerResponse(
                Guid.NewGuid(),
                request.LockerId,
                request.LockerCompartmentId,
                "DEV-01",
                1,
                ResponseResult,
                FailureReason,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
