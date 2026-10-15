using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Lockers;

public sealed class LockerServiceTests
{
    [Fact(Skip = "Blocked: LKR-01 in Excel requires LockerService.GetRegistrationOptionsAsync, absent from dev.")]
    [Trait("Category", "LKR-01")]
    public async Task GetRegistrationOptionsAsync_UTC01()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-01 in Excel requires LockerService.GetRegistrationOptionsAsync, absent from dev.")]
    [Trait("Category", "LKR-01")]
    public async Task GetRegistrationOptionsAsync_UTC02()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-01 in Excel requires LockerService.GetRegistrationOptionsAsync, absent from dev.")]
    [Trait("Category", "LKR-01")]
    public async Task GetRegistrationOptionsAsync_UTC03()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact]
    [Trait("Category", "LKR-02")]
    public async Task GetLockersAsync_UTC01()
    {
        await ListCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "LKR-02")]
    public async Task GetLockersAsync_UTC02()
    {
        await ListCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "LKR-02")]
    public async Task GetLockersAsync_UTC03()
    {
        await ListCaseAsync(3);
    }

    [Fact(Skip = "Blocked: LKR-03 in Excel requires LockerService.GetOperationalSummaryAsync, absent from dev.")]
    [Trait("Category", "LKR-03")]
    public async Task GetOperationalSummaryAsync_UTC01()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-03 in Excel requires LockerService.GetOperationalSummaryAsync, absent from dev.")]
    [Trait("Category", "LKR-03")]
    public async Task GetOperationalSummaryAsync_UTC02()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-03 in Excel requires LockerService.GetOperationalSummaryAsync, absent from dev.")]
    [Trait("Category", "LKR-03")]
    public async Task GetOperationalSummaryAsync_UTC03()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC01()
    {
        await DetailCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC02()
    {
        await DetailCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC03()
    {
        await DetailCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC04()
    {
        await DetailCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC05()
    {
        await DetailCaseAsync(5);
    }

    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC01()
    {
        await CreateCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC02()
    {
        await CreateCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC03()
    {
        await CreateCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC04()
    {
        await CreateCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC01()
    {
        await UpdateCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC02()
    {
        await UpdateCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC03()
    {
        await UpdateCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC04()
    {
        await UpdateCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC05()
    {
        await UpdateCaseAsync(5);
    }

    [Fact]
    [Trait("Category", "LKR-07")]
    public async Task GetCompartmentsAsync_UTC01()
    {
        await CompartmentsCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "LKR-07")]
    public async Task GetCompartmentsAsync_UTC02()
    {
        await CompartmentsCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "LKR-07")]
    public async Task GetCompartmentsAsync_UTC03()
    {
        await CompartmentsCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC01()
    {
        await CreateCompartmentCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC02()
    {
        await CreateCompartmentCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC03()
    {
        await CreateCompartmentCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC04()
    {
        await CreateCompartmentCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC05()
    {
        await CreateCompartmentCaseAsync(5);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC06()
    {
        await CreateCompartmentCaseAsync(6);
    }

    [Fact(Skip = "Blocked: LKR-09 in Excel requires LockerService.UpdateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC01()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-09 in Excel requires LockerService.UpdateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC02()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-09 in Excel requires LockerService.UpdateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC03()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-09 in Excel requires LockerService.UpdateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC04()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-10 in Excel requires LockerService.DeactivateLockerAsync, absent from dev.")]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC01()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-10 in Excel requires LockerService.DeactivateLockerAsync, absent from dev.")]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC02()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-10 in Excel requires LockerService.DeactivateLockerAsync, absent from dev.")]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC03()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-10 in Excel requires LockerService.DeactivateLockerAsync, absent from dev.")]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC04()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-11 in Excel requires LockerService.DeactivateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC01()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-11 in Excel requires LockerService.DeactivateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC02()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-11 in Excel requires LockerService.DeactivateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC03()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-11 in Excel requires LockerService.DeactivateCompartmentAsync, absent from dev.")]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC04()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-12 in Excel requires LockerService.UpdateOperationalStatusAsync, absent from dev.")]
    [Trait("Category", "LKR-12")]
    public async Task UpdateOperationalStatusAsync_UTC01()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-12 in Excel requires LockerService.UpdateOperationalStatusAsync, absent from dev.")]
    [Trait("Category", "LKR-12")]
    public async Task UpdateOperationalStatusAsync_UTC02()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact(Skip = "Blocked: LKR-12 in Excel requires LockerService.UpdateOperationalStatusAsync, absent from dev.")]
    [Trait("Category", "LKR-12")]
    public async Task UpdateOperationalStatusAsync_UTC03()
    {
        // Blocked marker only: implement this UTC after the required production API exists.
        await Task.CompletedTask;
        throw new NotSupportedException("This Excel scenario has no implementation on dev.");
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC01()
    {
        await CompartmentStatusCaseAsync(1);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC02()
    {
        await CompartmentStatusCaseAsync(2);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC03()
    {
        await CompartmentStatusCaseAsync(3);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC04()
    {
        await CompartmentStatusCaseAsync(4);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC05()
    {
        await CompartmentStatusCaseAsync(5);
    }


    private static async Task ListCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var locker = Locker();
        if (utc != 2)
        {
            db.AddRange(locker, Compartment(locker));
            await db.SaveChangesAsync();
        }
        var result = await new LockerService(db).GetLockersAsync(utc == 3 ? Guid.Empty : Guid.NewGuid(), utc == 3 ? "" : "Administrator", utc == 3 ? "" : "L01");
        if (utc == 2)
            Assert.Empty(result);
        else
        {
            var item = Assert.Single(result);
            Assert.Equal(locker.Id, item.Id);
            Assert.Equal(1, item.TotalCompartments);
            Assert.Equal(1, item.ActiveCompartments);
            // Excel requires a paged response. Preserve that contract assertion instead of accepting the current collection.
            Assert.IsType<PagedResult<LockerSummaryResponse>>(result);
        }
        Assert.Empty(db.AuditLogs);
    }
    private static async Task DetailCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        var service = new LockerService(db);
        if (utc == 1)
        {
            var result = await service.GetLockerByIdAsync(Guid.NewGuid(), "Administrator", locker.Id);
            Assert.Equal(locker.Id, result.Id);
            Assert.Equal(locker.DeviceIdentifier, result.DeviceIdentifier);
            Assert.Equal(compartment.Id, Assert.Single(result.Compartments).Id);
        }
        else if (utc == 2 || utc == 5)
        {
            Guid missing = utc == 2 ? Guid.NewGuid() : Guid.Empty;
            await Error<KeyNotFoundException>(() => service.GetLockerByIdAsync(Guid.NewGuid(), utc == 5 ? "" : "Administrator", missing), $"Locker với ID '{missing}' không tồn tại.");
        }
        else if (utc == 3)
            await Error<UnauthorizedAccessException>(() => service.GetLockerByIdAsync(Guid.NewGuid(), "LockerOperator", locker.Id), "Bạn không có quyền truy cập tủ locker này.");
        else
            await Error<UnauthorizedAccessException>(() => service.GetLockerByIdAsync(Guid.NewGuid(), "Resident", locker.Id), "Role 'Resident' không có quyền truy cập thông tin tủ locker.");
        Assert.Empty(db.AuditLogs);
    }
    private static async Task CreateCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var locker = Locker();
        db.Add(locker);
        await db.SaveChangesAsync();
        var service = new LockerService(db);
        var request = new CreateLockerRequest(utc == 2 ? locker.Code : " NEW ", " New address ", " Recovery address ", utc == 3 ? locker.DeviceIdentifier : " NEW-DEVICE ");
        if (utc == 1)
        {
            var result = await service.CreateLockerAsync(request);
            Assert.Equal("NEW", result.Code);
            Assert.Equal("New address", result.Address);
            Assert.Equal(LockerOperationalStatus.Operational, result.OperationalStatus);
            db.ChangeTracker.Clear();
            var saved = await db.Lockers.SingleAsync(l => l.Id == result.Id);
            Assert.Equal("NEW-DEVICE", saved.DeviceIdentifier);
            Assert.Equal("Recovery address", saved.RecoveryAddress);
            Assert.Equal(2, db.Lockers.Count());
        }
        else
        {
            if (utc == 2)
                await Error<InvalidOperationException>(() => service.CreateLockerAsync(request), $"Mã Locker '{locker.Code}' đã tồn tại trong hệ thống.");
            if (utc == 3)
                await Error<InvalidOperationException>(() => service.CreateLockerAsync(request), $"Mã định danh thiết bị IoT '{locker.DeviceIdentifier}' đã tồn tại.");
            if (utc == 4)
                await Assert.ThrowsAsync<NullReferenceException>(() => service.CreateLockerAsync(null!));
            db.ChangeTracker.Clear();
            Assert.Single(db.Lockers);
        }
        Assert.Empty(db.AuditLogs);
    }
    private static async Task UpdateCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var locker = Locker();
        var duplicate = Locker("L02");
        db.AddRange(locker, duplicate);
        await db.SaveChangesAsync();
        var service = new LockerService(db);
        var request = new UpdateLockerRequest(utc == 3 ? duplicate.Code : "UPDATED", "Updated address", "Updated recovery", utc == 4 ? duplicate.DeviceIdentifier : "UPDATED-DEVICE", LockerOperationalStatus.OutOfService);
        if (utc == 1)
        {
            var result = await service.UpdateLockerAsync(locker.Id, request);
            Assert.Equal(locker.Id, result.Id);
            Assert.Equal("UPDATED", result.Code);
            db.ChangeTracker.Clear();
            var saved = await db.Lockers.SingleAsync(l => l.Id == locker.Id);
            Assert.Equal("Updated address", saved.Address);
            Assert.Equal(LockerOperationalStatus.OutOfService, saved.OperationalStatus);
            Assert.Equal("UPDATED-DEVICE", saved.DeviceIdentifier);
        }
        else
        {
            if (utc == 2)
            {
                Guid missing = Guid.NewGuid();
                await Error<KeyNotFoundException>(() => service.UpdateLockerAsync(missing, request), $"Locker với ID '{missing}' không tồn tại.");
            }
            if (utc == 3)
                await Error<InvalidOperationException>(() => service.UpdateLockerAsync(locker.Id, request), $"Mã Locker '{duplicate.Code}' đã trùng với tủ khác.");
            if (utc == 4)
                await Error<InvalidOperationException>(() => service.UpdateLockerAsync(locker.Id, request), $"Mã định danh thiết bị '{duplicate.DeviceIdentifier}' đã trùng với tủ khác.");
            if (utc == 5)
                await Assert.ThrowsAsync<NullReferenceException>(() => service.UpdateLockerAsync(Guid.Empty, null!));
            db.ChangeTracker.Clear();
            Assert.Equal("L01", (await db.Lockers.SingleAsync(l => l.Id == locker.Id)).Code);
        }
        Assert.Empty(db.AuditLogs);
    }
    private static async Task CompartmentsCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        var service = new LockerService(db);
        if (utc == 2)
        {
            Guid missing = Guid.NewGuid();
            await Error<KeyNotFoundException>(() => service.GetCompartmentsAsync(Guid.NewGuid(), "Administrator", missing), $"Locker với ID '{missing}' không tồn tại.");
        }
        else
        {
            var result = await service.GetCompartmentsAsync(utc == 3 ? Guid.Empty : Guid.NewGuid(), utc == 3 ? "" : "Administrator", utc == 3 ? Guid.Empty : locker.Id);
            Assert.Equal(compartment.Id, Assert.Single(result).Id);
            Assert.IsType<PagedResult<LockerCompartmentResponse>>(result);
        }
        Assert.Empty(db.AuditLogs);
    }
    private static async Task CreateCompartmentCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var locker = Locker();
        var existing = Compartment(locker);
        if (utc == 3)
            locker.OperationalStatus = LockerOperationalStatus.Inactive;
        db.AddRange(locker, existing);
        await db.SaveChangesAsync();
        var service = new LockerService(db);
        var request = new CreateCompartmentRequest(utc == 4 ? existing.Code : " C02 ", utc == 5 ? existing.HardwareCode : " HW-C02 ", 2);
        if (utc == 1)
        {
            var result = await service.CreateCompartmentAsync(locker.Id, request);
            Assert.Equal("C02", result.Code);
            Assert.Equal(locker.Id, result.LockerId);
            db.ChangeTracker.Clear();
            var saved = await db.LockerCompartments.SingleAsync(c => c.Id == result.Id);
            Assert.Equal("HW-C02", saved.HardwareCode);
            Assert.Equal(2, saved.HardwareChannel);
            Assert.Equal(LockerCompartmentOperationalStatus.Operational, saved.OperationalStatus);
            Assert.Equal(2, db.LockerCompartments.Count());
        }
        else
        {
            if (utc == 2)
            {
                Guid missing = Guid.NewGuid();
                await Error<KeyNotFoundException>(() => service.CreateCompartmentAsync(missing, request), $"Locker với ID '{missing}' không tồn tại.");
            }
            if (utc == 3)
                await Error<InvalidOperationException>(() => service.CreateCompartmentAsync(locker.Id, request), "Tủ locker đã bị vô hiệu hóa (Inactive), không thể tạo thêm ngăn tủ.");
            if (utc == 4)
                await Error<InvalidOperationException>(() => service.CreateCompartmentAsync(locker.Id, request), $"Mã ngăn '{existing.Code}' đã tồn tại trong tủ locker này.");
            if (utc == 5)
                await Error<InvalidOperationException>(() => service.CreateCompartmentAsync(locker.Id, request), $"Mã phần cứng '{existing.HardwareCode}' đã tồn tại trong tủ locker này.");
            if (utc == 6)
                await Assert.ThrowsAsync<NullReferenceException>(() => service.CreateCompartmentAsync(Guid.Empty, null!));
            db.ChangeTracker.Clear();
            Assert.Single(db.LockerCompartments);
        }
        Assert.Empty(db.AuditLogs);
    }
    private static async Task CompartmentStatusCaseAsync(int utc)
    {
        await using var db = CreateDb();
        var locker = Locker();
        var compartment = Compartment(locker);
        if (utc == 3)
            locker.OperationalStatus = LockerOperationalStatus.Inactive;
        db.AddRange(locker, compartment);
        await db.SaveChangesAsync();
        var service = new LockerService(db);
        // This operation is named UpdateCompartmentStatusAsync in dev; retain the Excel method name in each UTC wrapper.
        var request = new UpdateCompartmentStatusRequest(utc == 2 ? (LockerCompartmentOperationalStatus)999 : LockerCompartmentOperationalStatus.OutOfService);
        if (utc == 1)
        {
            var result = await service.UpdateCompartmentStatusAsync(Guid.NewGuid(), "Administrator", locker.Id, compartment.Id, request);
            Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, result.OperationalStatus);
            db.ChangeTracker.Clear();
            Assert.Equal(result.OperationalStatus, (await db.LockerCompartments.SingleAsync()).OperationalStatus);
            Audit(db, "Compartment.StatusChanged", compartment.Id);
        }
        else
        {
            if (utc == 2)
                await Error<ArgumentException>(() => service.UpdateCompartmentStatusAsync(Guid.NewGuid(), "Administrator", locker.Id, compartment.Id, request), "Trạng thái ngăn tủ không hợp lệ.");
            if (utc == 3)
                await Error<InvalidOperationException>(() => service.UpdateCompartmentStatusAsync(Guid.NewGuid(), "Administrator", locker.Id, compartment.Id, request), "Tủ locker đã bị vô hiệu hóa (Inactive), không thể cập nhật trạng thái ngăn tủ.");
            if (utc == 4)
            {
                Guid missing = Guid.NewGuid();
                await Error<KeyNotFoundException>(() => service.UpdateCompartmentStatusAsync(Guid.NewGuid(), "Administrator", locker.Id, missing, request), $"Ngăn tủ với ID '{missing}' không tồn tại trong tủ locker này.");
            }
            if (utc == 5)
                await Assert.ThrowsAsync<NullReferenceException>(() => service.UpdateCompartmentStatusAsync(Guid.Empty, "", Guid.Empty, Guid.Empty, null!));
            db.ChangeTracker.Clear();
            Assert.Equal(LockerCompartmentOperationalStatus.Operational, (await db.LockerCompartments.SingleAsync()).OperationalStatus);
            Assert.Empty(db.AuditLogs);
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Locker Locker(string code = "L01") => new() { Id = Guid.NewGuid(), Code = code, Address = "Address", RecoveryAddress = "Recovery", DeviceIdentifier = "DEVICE-" + code, OperationalStatus = LockerOperationalStatus.Operational, ConnectionStatus = LockerConnectionStatus.Online, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
    private static LockerCompartment Compartment(Locker locker, string code = "C01") => new() { Id = Guid.NewGuid(), LockerId = locker.Id, Locker = locker, Code = code, HardwareCode = "HW-" + code, HardwareChannel = 1, OperationalStatus = LockerCompartmentOperationalStatus.Operational, DoorStatus = DoorStatus.Closed };
    private static async Task Error<T>(Func<Task> action, string message) where T : Exception
    {
        T exception = await Assert.ThrowsAsync<T>(action);
        if (exception is ArgumentException argument && argument.ParamName is not null)
            Assert.StartsWith(message, exception.Message);
        else
            Assert.Equal(message, exception.Message);
    }
    private static void Audit(ApplicationDbContext db, string action, Guid id, Guid? actor = null)
    {
        db.ChangeTracker.Clear();
        AuditLog log = Assert.Single(db.AuditLogs);
        Assert.Equal(action, log.Action);
        Assert.Equal(id, log.EntityId);
        Assert.Equal(actor, log.ActorUserId);
        Assert.Equal(AuditLogResult.Succeeded, log.Result);
    }
}
