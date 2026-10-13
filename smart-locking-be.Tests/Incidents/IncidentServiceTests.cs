using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Incidents;

public sealed class IncidentServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    #region INC-01: CreateGuestIncidentAsync
    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        GuestIncidentResponse response = await service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            graph.Locker.Code,
            "Locker",
            "Màn hình đơ",
            "Không bấm được phím nào trên màn hình",
            LockerCompartmentId: graph.Compartment.Id));

        Assert.Equal(IncidentStatus.Open, response.Status);
        Assert.StartsWith("INC-", response.ReferenceCode);
        Incident incident = await dbContext.Incidents.SingleAsync(i => i.Id == response.Id);
        Assert.Equal(IncidentSource.Guest, incident.Source);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Incident.GuestCreated");
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            graph.Locker.Code,
            "InvalidType",
            "Title",
            "Description",
            LockerCompartmentId: graph.Compartment.Id)));

        Assert.Equal("Incident type is invalid. (Parameter 'Type')", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            graph.Locker.Code,
            "Locker",
            "Title",
            "Description",
            DeliveryRequestId: Guid.NewGuid(),
            ReturnRequestId: Guid.NewGuid(),
            LockerCompartmentId: graph.Compartment.Id)));

        Assert.Equal("Only one delivery request or return request may be linked.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            "NON-EXISTING",
            "Locker",
            "Title",
            "Description")));

        Assert.Equal("Locker not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            graph.Locker.Code,
            "Locker",
            "Title",
            "Description",
            DeliveryRequestId: Guid.NewGuid())));

        Assert.Equal("Delivery request not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        await Assert.ThrowsAsync<NullReferenceException>(() => service.CreateGuestIncidentAsync(null!));
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var returnReq = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = graph.Locker.Id,
            Locker = graph.Locker,
            AllocatedCompartmentId = graph.Compartment.Id,
            AllocatedCompartment = graph.Compartment,
            ResidentProfileId = graph.Resident.ResidentProfile!.Id,
            ReturnCode = "RET-INC-01",
            Status = ReturnRequestStatus.Deposited,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ReturnRequests.Add(returnReq);
        await dbContext.SaveChangesAsync();

        var service = new IncidentService(dbContext, TimeProvider.System);
        GuestIncidentResponse response = await service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            graph.Locker.Code,
            "Return",
            "Sự cố trả hàng",
            "Mô tả sự cố",
            ReturnRequestId: returnReq.Id,
            LockerCompartmentId: null));

        Assert.NotNull(response);
        var incidentInDb = await dbContext.Incidents.SingleAsync(i => i.Id == response.Id);
        Assert.Equal(graph.Compartment.Id, incidentInDb.LockerCompartmentId);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Incident.GuestCreated");
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var otherLocker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LOCKER-OTHER",
            Address = "Address",
            RecoveryAddress = "Recovery",
            DeviceIdentifier = "dev-other"
        };
        var returnReq = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = otherLocker.Id,
            ResidentProfileId = graph.Resident.ResidentProfile!.Id,
            ReturnCode = "RET-INC-02",
            Status = ReturnRequestStatus.Deposited,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(otherLocker, returnReq);
        await dbContext.SaveChangesAsync();

        var service = new IncidentService(dbContext, TimeProvider.System);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            graph.Locker.Code,
            "Return",
            "Title",
            "Desc",
            ReturnRequestId: returnReq.Id)));

        Assert.Equal("Return request not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-01")]
    public async Task CreateGuestIncidentAsync_UTC09()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var otherCompartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = Guid.NewGuid(),
            Code = "B01",
            HardwareCode = "B01",
            HardwareChannel = 1
        };
        dbContext.LockerCompartments.Add(otherCompartment);
        await dbContext.SaveChangesAsync();

        var service = new IncidentService(dbContext, TimeProvider.System);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateGuestIncidentAsync(new CreateGuestIncidentRequest(
            graph.Locker.Code,
            "Compartment",
            "Title",
            "Desc",
            LockerCompartmentId: otherCompartment.Id)));

        Assert.Equal("Locker compartment not found.", ex.Message);
    }
    #endregion

    #region INC-02: CreateResidentIncidentAsync
    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC01()
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
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Incident.Created");
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("UnknownType", "Title", "Desc", LockerId: graph.Locker.Id)));

        Assert.Equal("Incident type is invalid. (Parameter 'Type')", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest(
                "Parcel", "Title", "Desc",
                ParcelId: graph.Parcel.Id,
                ReturnRequestId: Guid.NewGuid())));

        Assert.Equal("Only one parcel, return request, or payment may be linked.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateResidentIncidentAsync(
            Guid.NewGuid(),
            new CreateIncidentRequest("Parcel", "Title", "Desc")));

        Assert.Equal("Resident profile not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest(
                "Parcel", "Title", "Desc",
                LockerId: Guid.NewGuid(),
                ParcelId: graph.Parcel.Id)));

        Assert.Equal("Parcel does not belong to the selected locker.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateResidentIncidentAsync(Guid.Empty, null!));
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var returnReq = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = graph.Locker.Id,
            Locker = graph.Locker,
            AllocatedCompartmentId = graph.Compartment.Id,
            AllocatedCompartment = graph.Compartment,
            ResidentProfileId = graph.Resident.ResidentProfile!.Id,
            ReturnCode = "RET-RES-01",
            Status = ReturnRequestStatus.Deposited,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ReturnRequests.Add(returnReq);
        await dbContext.SaveChangesAsync();

        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse response = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest(
                "Return",
                "Không mở được ngăn trả",
                "Mô tả",
                ReturnRequestId: returnReq.Id,
                LockerId: null,
                LockerCompartmentId: null));

        Assert.Equal(graph.Locker.Id, response.LockerId);
        Assert.Equal(graph.Compartment.Id, response.LockerCompartmentId);
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC08()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var returnReq = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            LockerId = graph.Locker.Id,
            ResidentProfileId = Guid.NewGuid(),
            ReturnCode = "RET-OTHER",
            Status = ReturnRequestStatus.Deposited,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ReturnRequests.Add(returnReq);
        await dbContext.SaveChangesAsync();

        var service = new IncidentService(dbContext, TimeProvider.System);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Return", "Title", "Desc", ReturnRequestId: returnReq.Id)));

        Assert.Equal("Return request not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-02")]
    public async Task CreateResidentIncidentAsync_UTC09()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var charge = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            ParcelId = graph.Parcel.Id,
            Parcel = graph.Parcel,
            Amount = 10000,
            RatePerHourSnapshot = 5000,
            Currency = "VND",
            ChargeStartAt = DateTimeOffset.UtcNow.AddHours(-2),
            CalculatedThrough = DateTimeOffset.UtcNow,
            Status = OverdueChargeStatus.Paid,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var tx = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OverdueChargeId = charge.Id,
            OverdueCharge = charge,
            Amount = 10000,
            Currency = "VND",
            Status = PaymentTransactionStatus.Success,
            RequestedAt = DateTimeOffset.UtcNow
        };
        dbContext.AddRange(charge, tx);
        await dbContext.SaveChangesAsync();

        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse response = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Payment", "Lỗi trừ tiền", "Mô tả", PaymentTransactionId: tx.Id));

        Assert.Equal(graph.Parcel.Id, response.ParcelId);
        Assert.Equal(graph.Locker.Id, response.LockerId);
    }
    #endregion

    #region INC-03: GetResidentIncidentsAsync
    [Fact]
    [Trait("Category", "INC-03")]
    public async Task GetResidentIncidentsAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Compartment", "Cửa kẹt", "Không đóng được", LockerId: graph.Locker.Id));

        PagedResult<IncidentListItemResponse> result = await service.GetResidentIncidentsAsync(
            graph.Resident.Id, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "INC-03")]
    public async Task GetResidentIncidentsAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        PagedResult<IncidentListItemResponse> result = await service.GetResidentIncidentsAsync(
            Guid.NewGuid(), default, 1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "INC-03")]
    public async Task GetResidentIncidentsAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        PagedResult<IncidentListItemResponse> result = await service.GetResidentIncidentsAsync(
            Guid.Empty, default, 0, 0);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
    }
    #endregion

    #region INC-04: GetOperationalIncidentsAsync
    [Fact]
    [Trait("Category", "INC-04")]
    public async Task GetOperationalIncidentsAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Locker", "Hư màn hình", "Màn hình đen", LockerId: graph.Locker.Id));

        PagedResult<IncidentListItemResponse> result = await service.GetOperationalIncidentsAsync(
            graph.Operator.Id, nameof(UserRole.LockerOperator), IncidentStatus.Open, default, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "INC-04")]
    public async Task GetOperationalIncidentsAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        User unassignedOperator = CreateUser(UserRole.LockerOperator, "unassigned@boxora.test");
        dbContext.Users.Add(unassignedOperator);
        await dbContext.SaveChangesAsync();
        var service = new IncidentService(dbContext, TimeProvider.System);

        PagedResult<IncidentListItemResponse> result = await service.GetOperationalIncidentsAsync(
            unassignedOperator.Id, nameof(UserRole.LockerOperator), IncidentStatus.Open, default, 1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    [Trait("Category", "INC-04")]
    public async Task GetOperationalIncidentsAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        PagedResult<IncidentListItemResponse> result = await service.GetOperationalIncidentsAsync(
            graph.Operator.Id, nameof(UserRole.LockerOperator), null, default, 0, 0);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
    }
    #endregion

    #region INC-05: GetIncidentAsync
    [Fact]
    [Trait("Category", "INC-05")]
    public async Task GetIncidentAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Locker", "Lỗi loa", "Loa không kêu", LockerId: graph.Locker.Id));

        IncidentDetailResponse detail = await service.GetIncidentAsync(
            graph.Resident.Id, nameof(UserRole.Resident), created.Id, default);

        Assert.Equal(created.Id, detail.Id);
        Assert.Equal("Lỗi loa", detail.Title);
    }

    [Fact]
    [Trait("Category", "INC-05")]
    public async Task GetIncidentAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetIncidentAsync(
            Guid.NewGuid(), nameof(UserRole.Administrator), Guid.NewGuid(), default));

        Assert.Equal("Incident not found.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-05")]
    public async Task GetIncidentAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() => service.GetIncidentAsync(
            Guid.Empty, "", Guid.Empty, default));
    }
    #endregion

    #region INC-06: AddActionAsync
    [Fact]
    [Trait("Category", "INC-06")]
    public async Task AddActionAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Locker", "Kẹt cánh cửa", "Cửa bị lệch", LockerId: graph.Locker.Id));

        IncidentDetailResponse updated = await service.AddActionAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new AddIncidentActionRequest("Đang qua kiểm tra hiện trường."));

        Assert.Equal(2, updated.Actions.Count);
        Assert.Contains(updated.Actions, a => a.Notes == "Đang qua kiểm tra hiện trường.");
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Incident.ActionAdded");
    }

    [Fact]
    [Trait("Category", "INC-06")]
    public async Task AddActionAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Locker", "Kẹt cánh cửa", "Cửa bị lệch", LockerId: graph.Locker.Id));
        await service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Investigating, "Đang kiểm tra"));
        await service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Resolved, ResolutionSummary: "Đã khắc phục."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddActionAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new AddIncidentActionRequest("Ghi chú thêm")));

        Assert.Equal("Resolved incidents cannot receive new actions.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-06")]
    public async Task AddActionAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.AddActionAsync(
            Guid.Empty, "", Guid.Empty, null!));
    }

    [Fact]
    [Trait("Category", "INC-06")]
    public async Task AddActionAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Locker", "Kẹt cánh cửa", "Cửa bị lệch", LockerId: graph.Locker.Id));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.AddActionAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new AddIncidentActionRequest("")));

        Assert.Contains("Value is required.", ex.Message);
    }
    #endregion

    #region INC-07: UpdateStatusAsync
    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC01()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Compartment", "Cửa kẹt", "Không đóng được", LockerId: graph.Locker.Id));

        IncidentDetailResponse result = await service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Investigating, "Đang kiểm tra."));

        Assert.Equal(IncidentStatus.Investigating, result.Status);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Incident.StatusChanged");
    }

    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC02()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            Guid.NewGuid(),
            new UpdateIncidentStatusRequest((IncidentStatus)999, "Ghi chú")));

        Assert.Equal("Incident status is invalid. (Parameter 'Status')", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC03()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Compartment", "Cửa kẹt", "Không đóng được", LockerId: graph.Locker.Id));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Open, "Về Open lại")));

        Assert.Equal("Cannot change incident from Open to Open.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC04()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Compartment", "Cửa kẹt", "Không đóng được", LockerId: graph.Locker.Id));
        await service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Investigating, "Đang điều tra"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Resolved, ResolutionSummary: null)));

        Assert.Equal("Resolution summary is required when resolving an incident.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC05()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Compartment", "Cửa kẹt", "Không đóng được", LockerId: graph.Locker.Id));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Escalated, Notes: null)));

        Assert.Equal("Escalation notes are required.", ex.Message);
    }

    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC06()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        var service = new IncidentService(dbContext, TimeProvider.System);

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateStatusAsync(
            Guid.Empty, "", Guid.Empty, null!));
    }

    [Fact]
    [Trait("Category", "INC-07")]
    public async Task UpdateStatusAsync_UTC07()
    {
        await using ApplicationDbContext dbContext = CreateDbContext();
        TestGraph graph = await SeedGraphAsync(dbContext);
        User anotherAdmin = CreateUser(UserRole.Administrator, "otheradmin@boxora.test");
        dbContext.Users.Add(anotherAdmin);
        await dbContext.SaveChangesAsync();

        var service = new IncidentService(dbContext, TimeProvider.System);
        IncidentDetailResponse created = await service.CreateResidentIncidentAsync(
            graph.Resident.Id,
            new CreateIncidentRequest("Compartment", "Cửa kẹt", "Không đóng được", LockerId: graph.Locker.Id));

        IncidentDetailResponse result = await service.UpdateStatusAsync(
            graph.Operator.Id,
            nameof(UserRole.LockerOperator),
            created.Id,
            new UpdateIncidentStatusRequest(IncidentStatus.Escalated, Notes: "Escalate to administrator"));

        Assert.Equal(IncidentStatus.Escalated, result.Status);
        Assert.Contains(dbContext.Notifications, n => n.Type == "IncidentEscalated" && n.UserId == anotherAdmin.Id);
        Assert.Contains(dbContext.AuditLogs, a => a.Action == "Incident.StatusChanged");
    }
    #endregion

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
