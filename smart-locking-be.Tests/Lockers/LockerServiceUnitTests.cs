using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class LockerServiceUnitTests
{
    [Fact]
    public async Task CreateLocker_TrimsValuesAndCreatesCommissioningRecord()
    {
        await using var db = CreateDbContext();
        var actor = Guid.NewGuid();

        var result = await Service(db).CreateLockerAsync(actor,
            new CreateLockerRequest("  L-01  ", "  Main street  ", "  Recovery street  ", "  device-01  "));

        Assert.Equal("L-01", result.Code);
        Assert.Equal("Main street", result.Address);
        Assert.Equal(LockerOperationalStatus.Operational, result.OperationalStatus);
        var locker = await db.Lockers.SingleAsync();
        var commissioning = await db.Set<LockerCommissioning>().SingleAsync();
        Assert.Equal(locker.Id, commissioning.LockerId);
        Assert.Equal("device-01", commissioning.BoundDeviceIdentifier);
        Assert.Contains(db.AuditLogs, item => item.Action == "Locker.Created" && item.ActorUserId == actor);
    }

    [Fact]
    public async Task CreateLocker_RejectsDuplicateCodeAndDevice()
    {
        await using var db = CreateDbContext();
        db.Lockers.Add(Locker("L-01", "device-01"));
        await db.SaveChangesAsync();
        var service = Service(db);

        var duplicateCode = await Assert.ThrowsAsync<ConflictException>(() => service.CreateLockerAsync(Guid.NewGuid(),
            new CreateLockerRequest("L-01", "Address", "Recovery", "device-02")));
        var duplicateDevice = await Assert.ThrowsAsync<ConflictException>(() => service.CreateLockerAsync(Guid.NewGuid(),
            new CreateLockerRequest("L-02", "Address", "Recovery", "device-01")));

        Assert.Equal(LockerErrorCodes.CodeAlreadyExists, duplicateCode.ErrorCode);
        Assert.Equal(LockerErrorCodes.DeviceAlreadyExists, duplicateDevice.ErrorCode);
    }

    [Fact]
    public async Task GetPublicLocker_ReturnsOperationalLockerAndRejectsUnavailableLocker()
    {
        await using var db = CreateDbContext();
        var available = Locker("L-01", "device-01");
        var inactive = Locker("L-02", "device-02", LockerOperationalStatus.Inactive);
        db.Lockers.AddRange(available, inactive);
        await db.SaveChangesAsync();
        var service = Service(db);

        var result = await service.GetPublicLockerAsync(" L-01 ");

        Assert.Equal("L-01", result.Code);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetPublicLockerAsync("missing"));
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.GetPublicLockerAsync("L-02"));
        Assert.Equal(LockerErrorCodes.NotOperational, error.ErrorCode);
    }

    [Fact]
    public async Task GetLockers_AdministratorCanSearchAndOperatorOnlySeesAssignments()
    {
        await using var db = CreateDbContext();
        var first = Locker("L-01", "device-01");
        var second = Locker("L-02", "device-02");
        var operatorId = Guid.NewGuid();
        db.Lockers.AddRange(first, second);
        db.Users.Add(new User
        {
            Id = operatorId,
            Role = UserRole.LockerOperator,
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            LockerId = first.Id,
            OperatorUserId = operatorId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = Service(db);

        var adminResult = await service.GetLockersAsync(Guid.NewGuid(), nameof(UserRole.Administrator), "L-02");
        var operatorResult = await service.GetLockersAsync(operatorId, nameof(UserRole.LockerOperator));

        var adminLocker = Assert.Single(adminResult);
        Assert.Equal(second.Id, adminLocker.Id);
        var operatorLocker = Assert.Single(operatorResult);
        Assert.Equal(first.Id, operatorLocker.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetLockersAsync(Guid.NewGuid(), nameof(UserRole.Resident)));
    }

    [Fact]
    public async Task UpdateLocker_ChangesProfileAndWritesAudit()
    {
        await using var db = CreateDbContext();
        var locker = Locker("L-01", "device-01");
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();
        var actor = Guid.NewGuid();

        var result = await Service(db).UpdateLockerAsync(actor, locker.Id,
            new UpdateLockerRequest(" L-01A ", " New address ", " New recovery ", " device-01 ", LockerOperationalStatus.Operational));

        Assert.Equal("L-01A", result.Code);
        Assert.Equal("New address", result.Address);
        Assert.Equal("New recovery", result.RecoveryAddress);
        Assert.Contains(db.AuditLogs, item => item.Action == "Locker.Updated" && item.ActorUserId == actor);
    }

    [Fact]
    public async Task SoftDeleteLocker_MarksLockerInactive()
    {
        await using var db = CreateDbContext();
        var locker = Locker("L-01", "device-01");
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();

        Assert.True(await Service(db).SoftDeleteLockerAsync(Guid.NewGuid(), locker.Id));

        Assert.Equal(LockerOperationalStatus.Inactive, (await db.Lockers.SingleAsync()).OperationalStatus);
        Assert.Contains(db.AuditLogs, item => item.Action == "Locker.Deactivated");
    }

    [Fact]
    public async Task CompartmentCrud_TrimsValuesOrdersAndChangesStatus()
    {
        await using var db = CreateDbContext();
        var locker = Locker("L-01", "device-01");
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();
        var service = Service(db);

        var created = await service.CreateCompartmentAsync(Guid.NewGuid(), locker.Id,
            new CreateCompartmentRequest(" C02 ", " HW-02 ", 2));
        var first = await service.CreateCompartmentAsync(Guid.NewGuid(), locker.Id,
            new CreateCompartmentRequest(" C01 ", " HW-01 ", 1));
        var listed = await service.GetCompartmentsAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id);

        Assert.Equal("C02", created.Code);
        Assert.Equal("HW-02", created.HardwareCode);
        Assert.Collection(listed, item => Assert.Equal("C01", item.Code), item => Assert.Equal("C02", item.Code));

        var updated = await service.UpdateCompartmentAsync(Guid.NewGuid(), locker.Id, first.Id,
            new UpdateCompartmentRequest(" C01A ", " HW-01A ", 3));
        var status = await service.UpdateCompartmentStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id,
            first.Id, new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService));

        Assert.Equal("C01A", updated.Code);
        Assert.Equal(3, updated.HardwareChannel);
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, status.OperationalStatus);
    }

    [Fact]
    public async Task CreateCompartment_RejectsDuplicateHardwareMapping()
    {
        await using var db = CreateDbContext();
        var locker = Locker("L-01", "device-01");
        db.Lockers.Add(locker);
        await db.SaveChangesAsync();
        var service = Service(db);
        await service.CreateCompartmentAsync(Guid.NewGuid(), locker.Id, new CreateCompartmentRequest("C01", "HW-01", 1));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CreateCompartmentAsync(Guid.NewGuid(), locker.Id,
            new CreateCompartmentRequest("C02", "HW-01", 2)));

        Assert.Equal(LockerErrorCodes.CompartmentAlreadyExists, error.ErrorCode);
    }

    private static LockerService Service(ApplicationDbContext db) => new(db);

    private static ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Locker Locker(string code, string device, LockerOperationalStatus status = LockerOperationalStatus.Operational) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Address = $"{code} address",
        RecoveryAddress = $"{code} recovery",
        DeviceIdentifier = device,
        OperationalStatus = status,
        ConnectionStatus = LockerConnectionStatus.Online,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
