using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class LockerServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateLockerAsync_WithValidData_CreatesAndReturnsLockerDetail()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var request = new CreateLockerRequest(
            Code: "LCK-001",
            Address: "123 Le Loi, Quan 1, TP.HCM",
            RecoveryAddress: "Kho 456 Nguyen Hue, Quan 1, TP.HCM",
            DeviceIdentifier: "DEV-ESP32-001"
        );

        var response = await service.CreateLockerAsync(request);

        Assert.NotNull(response);
        Assert.Equal("LCK-001", response.Code);
        Assert.Equal("123 Le Loi, Quan 1, TP.HCM", response.Address);
        Assert.Equal("Kho 456 Nguyen Hue, Quan 1, TP.HCM", response.RecoveryAddress);
        Assert.Equal("DEV-ESP32-001", response.DeviceIdentifier);
        Assert.Equal(LockerOperationalStatus.Operational, response.OperationalStatus);
        Assert.Equal(LockerConnectionStatus.Unknown, response.ConnectionStatus);

        var persisted = await dbContext.Lockers.FirstOrDefaultAsync(l => l.Id == response.Id);
        Assert.NotNull(persisted);
        Assert.Equal("LCK-001", persisted.Code);
    }

    [Fact]
    public async Task CreateLockerAsync_WithDuplicateCode_ThrowsInvalidOperationException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var request1 = new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001");
        await service.CreateLockerAsync(request1);

        var request2 = new CreateLockerRequest("LCK-001", "Addr 2", "Rec 2", "DEV-002");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateLockerAsync(request2));
    }

    [Fact]
    public async Task GetLockersAsync_AsAdmin_ReturnsAllLockers()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        await service.CreateLockerAsync(new CreateLockerRequest("LCK-002", "Addr 2", "Rec 2", "DEV-002"));

        var adminId = Guid.NewGuid();
        var lockers = await service.GetLockersAsync(adminId, nameof(UserRole.Administrator));

        Assert.Equal(2, lockers.Count);
    }

    [Fact]
    public async Task GetLockersAsync_AsOperator_ReturnsOnlyAssignedLockers()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var locker1 = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        await service.CreateLockerAsync(new CreateLockerRequest("LCK-002", "Addr 2", "Rec 2", "DEV-002"));

        var operatorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        dbContext.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorId,
            LockerId = locker1.Id,
            AssignedByUserId = adminId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var lockers = await service.GetLockersAsync(operatorId, nameof(UserRole.LockerOperator));

        Assert.Single(lockers);
        Assert.Equal(locker1.Id, lockers.First().Id);
    }

    [Fact]
    public async Task UpdateLockerAsync_UpdatesFieldsSuccessfully()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var created = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Old Addr", "Old Rec", "DEV-001"));

        var updateRequest = new UpdateLockerRequest(
            Code: "LCK-001-UPD",
            Address: "New Addr",
            RecoveryAddress: "New Rec",
            DeviceIdentifier: "DEV-001-UPD",
            OperationalStatus: LockerOperationalStatus.OutOfService
        );

        var updated = await service.UpdateLockerAsync(created.Id, updateRequest);

        Assert.Equal("LCK-001-UPD", updated.Code);
        Assert.Equal("New Addr", updated.Address);
        Assert.Equal(LockerOperationalStatus.OutOfService, updated.OperationalStatus);
    }

    [Fact]
    public async Task UpdateCompartmentStatusAsync_AsAssignedOperator_UpdatesStatusSuccessfully()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        var compartment = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var operatorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        dbContext.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorId,
            LockerId = locker.Id,
            AssignedByUserId = adminId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var updateReq = new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService);
        var result = await service.UpdateCompartmentStatusAsync(operatorId, nameof(UserRole.LockerOperator), locker.Id, compartment.Id, updateReq);

        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, result.OperationalStatus);
    }

    [Fact]
    public async Task UpdateCompartmentStatusAsync_AsUnassignedOperator_ThrowsUnauthorizedAccessException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        var compartment = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var unassignedOperatorId = Guid.NewGuid();
        var updateReq = new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateCompartmentStatusAsync(unassignedOperatorId, nameof(UserRole.LockerOperator), locker.Id, compartment.Id, updateReq));
    }

    [Fact]
    public async Task SoftDeleteLockerAsync_SetsOperationalStatusToInactive()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var created = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));

        var result = await service.SoftDeleteLockerAsync(created.Id);
        Assert.True(result);

        var locker = await dbContext.Lockers.FirstAsync(l => l.Id == created.Id);
        Assert.Equal(LockerOperationalStatus.Inactive, locker.OperationalStatus);
    }

    [Fact]
    public async Task CreateCompartmentAsync_AddsCompartmentToLocker()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));

        var req = new CreateCompartmentRequest("A01", "HW-CH-01", 1);
        var compartment = await service.CreateCompartmentAsync(locker.Id, req);

        Assert.NotNull(compartment);
        Assert.Equal("A01", compartment.Code);
        Assert.Equal("HW-CH-01", compartment.HardwareCode);
        Assert.Equal(1, compartment.HardwareChannel);
        Assert.Equal(LockerCompartmentOperationalStatus.Operational, compartment.OperationalStatus);
        Assert.Equal(DoorStatus.Unknown, compartment.DoorStatus);
    }

    [Fact]
    public async Task UpdateCompartmentStatusAsync_AsAdmin_UpdatesStatusSuccessfully()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        var compartment = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var adminId = Guid.NewGuid();
        var updateReq = new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService);
        var result = await service.UpdateCompartmentStatusAsync(adminId, nameof(UserRole.Administrator), locker.Id, compartment.Id, updateReq);

        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, result.OperationalStatus);
    }

    [Fact]
    public async Task UpdateCompartmentStatusAsync_LockerNotFound_ThrowsKeyNotFoundException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var adminId = Guid.NewGuid();
        var updateReq = new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateCompartmentStatusAsync(adminId, nameof(UserRole.Administrator), Guid.NewGuid(), Guid.NewGuid(), updateReq));
    }

    [Fact]
    public async Task UpdateCompartmentStatusAsync_CompartmentNotFound_ThrowsKeyNotFoundException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        var adminId = Guid.NewGuid();
        var updateReq = new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateCompartmentStatusAsync(adminId, nameof(UserRole.Administrator), locker.Id, Guid.NewGuid(), updateReq));
    }

    [Theory]
    [InlineData(nameof(UserRole.Resident))]
    [InlineData("")]
    [InlineData("UnknownRole")]
    public async Task GetLockersAsync_AsInvalidRole_ThrowsUnauthorizedAccessException(string role)
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetLockersAsync(Guid.NewGuid(), role));
    }

    [Theory]
    [InlineData(nameof(UserRole.Resident))]
    [InlineData("")]
    public async Task GetLockerByIdAsync_AsInvalidRole_ThrowsUnauthorizedAccessException(string role)
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetLockerByIdAsync(Guid.NewGuid(), role, locker.Id));
    }

    [Theory]
    [InlineData(nameof(UserRole.Resident))]
    [InlineData("")]
    public async Task UpdateCompartmentStatusAsync_AsInvalidRole_ThrowsUnauthorizedAccessException(string role)
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        var compartment = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));
        var updateReq = new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateCompartmentStatusAsync(Guid.NewGuid(), role, locker.Id, compartment.Id, updateReq));
    }

    [Theory]
    [InlineData("", "Address", "RecoveryAddress", "Device")]
    [InlineData("Code", "   ", "RecoveryAddress", "Device")]
    [InlineData("Code", "Address", "", "Device")]
    [InlineData("Code", "Address", "RecoveryAddress", "   ")]
    public async Task CreateLockerAsync_WithWhitespaceOrEmptyFields_ThrowsArgumentException(string code, string addr, string rec, string dev)
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateLockerAsync(new CreateLockerRequest(code, addr, rec, dev)));
    }

    [Fact]
    public async Task CreateLockerAsync_TrimsInputDataSuccessfully()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var result = await service.CreateLockerAsync(new CreateLockerRequest("  LCK-TRIM  ", "  Addr  ", "  Rec  ", "  DEV-01  "));

        Assert.Equal("LCK-TRIM", result.Code);
        Assert.Equal("Addr", result.Address);
        Assert.Equal("Rec", result.RecoveryAddress);
        Assert.Equal("DEV-01", result.DeviceIdentifier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateCompartmentAsync_WithZeroOrNegativeHardwareChannel_ThrowsArgumentException(int channel)
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", channel)));
    }

    [Fact]
    public async Task CreateCompartmentAsync_WithDuplicateHardwareChannel_ThrowsInvalidOperationException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));

        await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A02", "HW-02", 1)));
    }

    [Fact]
    public async Task UpdateLockerAsync_WithInvalidEnumStatus_ThrowsArgumentException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));

        var invalidStatusRequest = new UpdateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001", (LockerOperationalStatus)999);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateLockerAsync(locker.Id, invalidStatusRequest));
    }

    [Fact]
    public async Task UpdateCompartmentStatusAsync_WithInvalidEnumStatus_ThrowsArgumentException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        var compartment = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var invalidStatusRequest = new UpdateCompartmentStatusRequest((LockerCompartmentOperationalStatus)999);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateCompartmentStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id, compartment.Id, invalidStatusRequest));
    }

    [Fact]
    public async Task UpdateCompartmentStatusAsync_WhenLockerIsInactive_ThrowsInvalidOperationException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));
        var compartment = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var operatorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        dbContext.OperatorAssignments.Add(new OperatorAssignment
        {
            Id = Guid.NewGuid(),
            OperatorUserId = operatorId,
            LockerId = locker.Id,
            AssignedByUserId = adminId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        // Soft-delete locker
        await service.SoftDeleteLockerAsync(locker.Id);

        var updateReq = new UpdateCompartmentStatusRequest(LockerCompartmentOperationalStatus.OutOfService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateCompartmentStatusAsync(operatorId, nameof(UserRole.LockerOperator), locker.Id, compartment.Id, updateReq));
    }

    [Fact]
    public async Task CreateCompartmentAsync_WhenLockerIsInactive_ThrowsInvalidOperationException()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-001", "Addr 1", "Rec 1", "DEV-001"));

        // Soft-delete locker
        await service.SoftDeleteLockerAsync(locker.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1)));
    }
}
