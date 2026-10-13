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
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    #region LKR-01: GetRegistrationOptionsAsync
    [Fact]
    [Trait("Category", "LKR-01")]
    public async Task GetRegistrationOptionsAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-REG-01", "Addr", "Rec", "DEV-REG-01"));

        var options = await service.GetRegistrationOptionsAsync();
        Assert.Contains(options, o => o.Id == locker.Id);
    }

    [Fact]
    [Trait("Category", "LKR-01")]
    public async Task GetRegistrationOptionsAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var options = await service.GetRegistrationOptionsAsync();
        Assert.Empty(options);
    }

    [Fact]
    [Trait("Category", "LKR-01")]
    public async Task GetRegistrationOptionsAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        await service.CreateLockerAsync(new CreateLockerRequest("LCK-REG-02", "Addr2", "Rec2", "DEV-REG-02"));

        var options = await service.GetRegistrationOptionsAsync();
        Assert.NotEmpty(options);
    }
    #endregion

    #region LKR-02: GetLockersAsync
    [Fact]
    [Trait("Category", "LKR-02")]
    public async Task GetLockersAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        await service.CreateLockerAsync(new CreateLockerRequest("LCK-LST-01", "Addr", "Rec", "DEV-LST-01"));

        var paged = await service.GetLockersAsync(Guid.NewGuid(), nameof(UserRole.Administrator), "LCK-LST", default, 1, 10);
        Assert.True(paged.TotalCount >= 1);
    }

    [Fact]
    [Trait("Category", "LKR-02")]
    public async Task GetLockersAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetLockersAsync(Guid.NewGuid(), nameof(UserRole.Resident)));
    }

    [Fact]
    [Trait("Category", "LKR-02")]
    public async Task GetLockersAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var paged = await service.GetLockersAsync(Guid.Empty, nameof(UserRole.Administrator), "", default, 0, 0);
        Assert.Equal(0, paged.TotalCount);
    }
    #endregion

    #region LKR-03: GetOperationalSummaryAsync
    [Fact]
    [Trait("Category", "LKR-03")]
    public async Task GetOperationalSummaryAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        await service.CreateLockerAsync(new CreateLockerRequest("LCK-OPS-01", "Addr", "Rec", "DEV-OPS-01"));

        var summary = await service.GetOperationalSummaryAsync(Guid.NewGuid(), nameof(UserRole.Administrator), default, 1, 10);
        Assert.NotNull(summary);
        Assert.True(summary.TotalCount >= 1);
    }

    [Fact]
    [Trait("Category", "LKR-03")]
    public async Task GetOperationalSummaryAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetOperationalSummaryAsync(Guid.NewGuid(), nameof(UserRole.Resident)));
    }

    [Fact]
    [Trait("Category", "LKR-03")]
    public async Task GetOperationalSummaryAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var summary = await service.GetOperationalSummaryAsync(Guid.Empty, nameof(UserRole.Administrator), default, 0, 0);
        Assert.NotNull(summary);
        Assert.Equal(0, summary.TotalCount);
    }
    #endregion

    #region LKR-04: GetLockerByIdAsync
    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-BYID-01", "Addr", "Rec", "DEV-BYID-01"));

        var detail = await service.GetLockerByIdAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id);
        Assert.Equal(locker.Id, detail.Id);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var missingId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetLockerByIdAsync(Guid.NewGuid(), nameof(UserRole.Administrator), missingId));
        Assert.Contains(missingId.ToString(), ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-BYID-03", "Addr", "Rec", "DEV-BYID-03"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetLockerByIdAsync(Guid.NewGuid(), nameof(UserRole.LockerOperator), locker.Id));
        Assert.Equal("Bạn không có quyền truy cập tủ locker này.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-BYID-04", "Addr", "Rec", "DEV-BYID-04"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetLockerByIdAsync(Guid.NewGuid(), nameof(UserRole.Resident), locker.Id));
        Assert.Contains("không có quyền truy cập thông tin tủ locker", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-04")]
    public async Task GetLockerByIdAsync_UTC05()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetLockerByIdAsync(Guid.Empty, nameof(UserRole.Administrator), Guid.Empty));
    }
    #endregion

    #region LKR-05: CreateLockerAsync
    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var created = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CRT-01", "Addr", "Rec", "DEV-CRT-01"));
        Assert.Equal("LCK-CRT-01", created.Code);
    }

    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        await service.CreateLockerAsync(new CreateLockerRequest("LCK-CRT-02", "Addr", "Rec", "DEV-CRT-02"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateLockerAsync(new CreateLockerRequest("LCK-CRT-02", "Addr2", "Rec2", "DEV-CRT-02B")));
        Assert.Contains("đã tồn tại trong hệ thống", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        await service.CreateLockerAsync(new CreateLockerRequest("LCK-CRT-03A", "Addr", "Rec", "DEV-CRT-03"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateLockerAsync(new CreateLockerRequest("LCK-CRT-03B", "Addr2", "Rec2", "DEV-CRT-03")));
        Assert.Contains("đã tồn tại", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-05")]
    public async Task CreateLockerAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.CreateLockerAsync(new CreateLockerRequest("", "", "", "")));
    }
    #endregion

    #region LKR-06: UpdateLockerAsync
    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UPD-01", "Addr", "Rec", "DEV-UPD-01"));

        var updated = await service.UpdateLockerAsync(locker.Id, new UpdateLockerRequest("LCK-UPD-01B", "AddrNew", "RecNew", "DEV-UPD-01B"));
        Assert.Equal("LCK-UPD-01B", updated.Code);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var missingId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateLockerAsync(missingId, new UpdateLockerRequest("LCK-UPD-02", "Addr", "Rec", "DEV-UPD-02")));
        Assert.Contains(missingId.ToString(), ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker1 = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UPD-03A", "Addr", "Rec", "DEV-UPD-03A"));
        var locker2 = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UPD-03B", "Addr", "Rec", "DEV-UPD-03B"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateLockerAsync(locker2.Id, new UpdateLockerRequest("LCK-UPD-03A", "Addr", "Rec", "DEV-UPD-03B-NEW")));
        Assert.Contains("đã trùng với tủ khác", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker1 = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UPD-04A", "Addr", "Rec", "DEV-UPD-04A"));
        var locker2 = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UPD-04B", "Addr", "Rec", "DEV-UPD-04B"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateLockerAsync(locker2.Id, new UpdateLockerRequest("LCK-UPD-04B-NEW", "Addr", "Rec", "DEV-UPD-04A")));
        Assert.Contains("đã trùng với tủ khác", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-06")]
    public async Task UpdateLockerAsync_UTC05()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.UpdateLockerAsync(Guid.Empty, null!));
    }
    #endregion

    #region LKR-07: GetCompartmentsAsync
    [Fact]
    [Trait("Category", "LKR-07")]
    public async Task GetCompartmentsAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CMP-01", "Addr", "Rec", "DEV-CMP-01"));
        await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var res = await service.GetCompartmentsAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id, default, 1, 10);
        Assert.Equal(1, res.TotalCount);
    }

    [Fact]
    [Trait("Category", "LKR-07")]
    public async Task GetCompartmentsAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetCompartmentsAsync(Guid.NewGuid(), nameof(UserRole.Administrator), Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "LKR-07")]
    public async Task GetCompartmentsAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CMP-03", "Addr", "Rec", "DEV-CMP-03"));

        var res = await service.GetCompartmentsAsync(Guid.Empty, nameof(UserRole.Administrator), locker.Id, default, 0, 0);
        Assert.Equal(0, res.TotalCount);
    }
    #endregion

    #region LKR-08: CreateCompartmentAsync
    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CCMP-01", "Addr", "Rec", "DEV-CCMP-01"));

        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));
        Assert.Equal("A01", comp.Code);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var missingId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CreateCompartmentAsync(missingId, new CreateCompartmentRequest("A01", "HW-01", 1)));
        Assert.Contains(missingId.ToString(), ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CCMP-03", "Addr", "Rec", "DEV-CCMP-03"));
        await service.UpdateOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id, new UpdateOperationalStatusRequest(nameof(LockerOperationalStatus.Inactive), "Test"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1)));
        Assert.Contains("vô hiệu hóa", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CCMP-04", "Addr", "Rec", "DEV-CCMP-04"));
        await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-02", 2)));
        Assert.Contains("Mã ngăn 'A01' đã tồn tại", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC05()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CCMP-05", "Addr", "Rec", "DEV-CCMP-05"));
        await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A02", "HW-01", 2)));
        Assert.Contains("Mã phần cứng 'HW-01' đã tồn tại", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-08")]
    public async Task CreateCompartmentAsync_UTC06()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-CCMP-06", "Addr", "Rec", "DEV-CCMP-06"));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("", "", 0)));
    }
    #endregion

    #region LKR-09: UpdateCompartmentAsync
    [Fact]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UCMP-01", "Addr", "Rec", "DEV-UCMP-01"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var updated = await service.UpdateCompartmentAsync(Guid.NewGuid(), locker.Id, comp.Id, new UpdateCompartmentRequest("A01-UPD", "HW-01-UPD", 1));
        Assert.Equal("A01-UPD", updated.Code);
    }

    [Fact]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UCMP-02", "Addr", "Rec", "DEV-UCMP-02"));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateCompartmentAsync(Guid.NewGuid(), locker.Id, Guid.NewGuid(), new UpdateCompartmentRequest("A01", "HW-01", 1)));
        Assert.Equal("Không tìm thấy ngăn tủ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UCMP-03", "Addr", "Rec", "DEV-UCMP-03"));
        var comp1 = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));
        var comp2 = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A02", "HW-02", 2));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateCompartmentAsync(Guid.NewGuid(), locker.Id, comp2.Id, new UpdateCompartmentRequest("A01", "HW-02-UPD", 2)));
        Assert.Equal("Mã ngăn, mã phần cứng hoặc kênh phần cứng đã được sử dụng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-09")]
    public async Task UpdateCompartmentAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.UpdateCompartmentAsync(Guid.Empty, Guid.Empty, Guid.Empty, null!));
    }
    #endregion

    #region LKR-10: DeactivateLockerAsync
    [Fact]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-DEA-01", "Addr", "Rec", "DEV-DEA-01"));

        var res = await service.DeactivateLockerAsync(Guid.NewGuid(), locker.Id, "Bảo trì định kỳ");
        Assert.Equal(LockerOperationalStatus.Inactive, res.OperationalStatus);
    }

    [Fact]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeactivateLockerAsync(Guid.NewGuid(), Guid.NewGuid(), "Lý do"));
        Assert.Equal("Không tìm thấy locker.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-DEA-03", "Addr", "Rec", "DEV-DEA-03"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        // Add active parcel
        var deliv = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            AllocatedCompartmentId = comp.Id,
            Status = DeliveryRequestStatus.Deposited
        };
        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliv.Id,
            ParcelCode = "P-ACTIVE",
            Status = ParcelStatus.Stored
        };
        dbContext.AddRange(deliv, parcel);
        await dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeactivateLockerAsync(Guid.NewGuid(), locker.Id, "Bảo trì"));
        Assert.Equal("Locker còn parcel, return hoặc reservation đang hoạt động.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-10")]
    public async Task DeactivateLockerAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-DEA-04", "Addr", "Rec", "DEV-DEA-04"));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.DeactivateLockerAsync(Guid.Empty, locker.Id, ""));
    }
    #endregion

    #region LKR-11: DeactivateCompartmentAsync
    [Fact]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-DEAC-01", "Addr", "Rec", "DEV-DEAC-01"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var res = await service.DeactivateCompartmentAsync(Guid.NewGuid(), locker.Id, comp.Id, "Hỏng khóa");
        Assert.Equal(LockerCompartmentOperationalStatus.Inactive, res.OperationalStatus);
    }

    [Fact]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-DEAC-02", "Addr", "Rec", "DEV-DEAC-02"));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeactivateCompartmentAsync(Guid.NewGuid(), locker.Id, Guid.NewGuid(), "Lý do"));
        Assert.Equal("Không tìm thấy ngăn tủ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-DEAC-03", "Addr", "Rec", "DEV-DEAC-03"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        // Add active parcel in this compartment
        var deliv = new DeliveryRequest
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            AllocatedCompartmentId = comp.Id,
            Status = DeliveryRequestStatus.Deposited
        };
        var parcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliv.Id,
            ParcelCode = "P-ACTIVE",
            Status = ParcelStatus.Stored
        };
        dbContext.AddRange(deliv, parcel);
        await dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeactivateCompartmentAsync(Guid.NewGuid(), locker.Id, comp.Id, "Hỏng"));
        Assert.Equal("Ngăn tủ còn parcel, return hoặc reservation đang hoạt động.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-11")]
    public async Task DeactivateCompartmentAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-DEAC-04", "Addr", "Rec", "DEV-DEAC-04"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.DeactivateCompartmentAsync(Guid.Empty, locker.Id, comp.Id, ""));
    }
    #endregion

    #region LKR-12: UpdateOperationalStatusAsync
    [Fact]
    [Trait("Category", "LKR-12")]
    public async Task UpdateOperationalStatusAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UOPS-01", "Addr", "Rec", "DEV-UOPS-01"));

        var res = await service.UpdateOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id,
            new UpdateOperationalStatusRequest(nameof(LockerOperationalStatus.OutOfService), "Bảo trì"));
        Assert.Equal(LockerOperationalStatus.OutOfService, res.OperationalStatus);
    }

    [Fact]
    [Trait("Category", "LKR-12")]
    public async Task UpdateOperationalStatusAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UOPS-02", "Addr", "Rec", "DEV-UOPS-02"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id,
                new UpdateOperationalStatusRequest("INVALID_STATUS", "Lý do")));
        Assert.Contains("Trạng thái tủ không hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-12")]
    public async Task UpdateOperationalStatusAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateOperationalStatusAsync(Guid.Empty, nameof(UserRole.Administrator), Guid.Empty,
                new UpdateOperationalStatusRequest(nameof(LockerOperationalStatus.Operational), "")));
    }
    #endregion

    #region LKR-13: UpdateCompartmentOperationalStatusAsync
    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC01()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UCOPS-01", "Addr", "Rec", "DEV-UCOPS-01"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var res = await service.UpdateCompartmentOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id, comp.Id,
            new UpdateOperationalStatusRequest(nameof(LockerCompartmentOperationalStatus.OutOfService), "Hỏng ngăn"));
        Assert.Equal(LockerCompartmentOperationalStatus.OutOfService, res.OperationalStatus);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC02()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UCOPS-02", "Addr", "Rec", "DEV-UCOPS-02"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateCompartmentOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id, comp.Id,
                new UpdateOperationalStatusRequest("INVALID_STATUS", "Lý do")));
        Assert.Contains("Trạng thái ngăn tủ không hợp lệ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC03()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UCOPS-03", "Addr", "Rec", "DEV-UCOPS-03"));
        var comp = await service.CreateCompartmentAsync(locker.Id, new CreateCompartmentRequest("A01", "HW-01", 1));
        await service.UpdateOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id,
            new UpdateOperationalStatusRequest(nameof(LockerOperationalStatus.Inactive), "Tủ ngưng hoạt động"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateCompartmentOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id, comp.Id,
                new UpdateOperationalStatusRequest(nameof(LockerCompartmentOperationalStatus.OutOfService), "Lý do")));
        Assert.Equal("Tủ locker đã bị vô hiệu hóa (Inactive), không thể cập nhật trạng thái ngăn tủ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC04()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);
        var locker = await service.CreateLockerAsync(new CreateLockerRequest("LCK-UCOPS-04", "Addr", "Rec", "DEV-UCOPS-04"));
        var missingCompId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateCompartmentOperationalStatusAsync(Guid.NewGuid(), nameof(UserRole.Administrator), locker.Id, missingCompId,
                new UpdateOperationalStatusRequest(nameof(LockerCompartmentOperationalStatus.OutOfService), "Lý do")));
        Assert.Contains(missingCompId.ToString(), ex.Message);
    }

    [Fact]
    [Trait("Category", "LKR-13")]
    public async Task UpdateCompartmentOperationalStatusAsync_UTC05()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new LockerService(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateCompartmentOperationalStatusAsync(Guid.Empty, nameof(UserRole.Administrator), Guid.Empty, Guid.Empty,
                new UpdateOperationalStatusRequest(nameof(LockerCompartmentOperationalStatus.Operational), "")));
    }
    #endregion
}
