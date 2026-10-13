using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using System.Security.Authentication;

namespace smart_locking_be.Tests.DeliveryRequests;

public sealed class DeliveryRequestServiceTests
{
    #region DEL-01: CreateAsync
    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        InitiateDeliveryResponse res = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        Assert.Equal(DeliveryRequestStatus.Started, res.Status);
        Assert.NotNull(res.GuestSessionToken);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(new InitiateDeliveryRequest("NON_EXISTENT")));
        Assert.Equal("Locker với mã 'NON_EXISTENT' không tồn tại.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        locker.OperationalStatus = LockerOperationalStatus.OutOfService;
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new InitiateDeliveryRequest(locker.Code)));
        Assert.Equal("Locker hiện không sẵn sàng nhận hàng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        policy.IsActive = false;
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new InitiateDeliveryRequest(locker.Code)));
        Assert.Equal("Không có chính sách hệ thống đang hiệu lực.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(null!));
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new InitiateDeliveryRequest("")));
        Assert.Contains("Giá trị không được để trống.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        string longCode = new('A', 51);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new InitiateDeliveryRequest(longCode)));
        Assert.Contains("Giá trị không được vượt quá 50 ký tự.", ex.Message);
    }
    #endregion

    #region DEL-02: SubmitAsync
    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333", DeliveryApprovalMode.Auto);
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        DeliveryRequestSummaryResponse res = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333"));

        Assert.Equal(DeliveryRequestStatus.Approved, res.Status);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0999888777")));
        Assert.Equal("Không tìm thấy Cư dân đang hoạt động với số điện thoại đã nhập.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        resident.RegisteredLockerId = Guid.NewGuid();
        await db.SaveChangesAsync();
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333")));
        Assert.Equal("Cư dân không đăng ký nhận hàng tại locker này.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.SubmitAsync(Guid.Empty, "", null!));
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        await SeedResidentAsync(db, "0911222333");
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.SubmitAsync(
            initiated.Id,
            "",
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333")));
        Assert.Equal("Thiếu X-Guest-Session-Token.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        await SeedResidentAsync(db, "0911222333");
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("relative/path.jpg", "0911222333")));
        Assert.Contains("ParcelImageUrl phải là URL HTTP hoặc HTTPS tuyệt đối.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        await SeedResidentAsync(db, "0911222333", DeliveryApprovalMode.Auto);
        var pushService = new RecordingPushNotificationService();
        DeliveryRequestService service = CreateService(db, pushNotificationService: pushService);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        DeliveryRequestSummaryResponse res = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333"));

        Assert.Equal(DeliveryRequestStatus.Approved, res.Status);
        Assert.Empty(pushService.Requests);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC08()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333", DeliveryApprovalMode.Manual);
        var pushService = new RecordingPushNotificationService();
        DeliveryRequestService service = CreateService(db, pushNotificationService: pushService);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        DeliveryRequestSummaryResponse res = await service.SubmitAsync(
            initiated.Id,
            initiated.GuestSessionToken,
            new SubmitDeliveryRequest("https://cdn.example.com/parcel.jpg", "0911222333"));

        Assert.Equal(DeliveryRequestStatus.PendingApproval, res.Status);
        var entity = await db.DeliveryRequests.SingleAsync(r => r.Id == initiated.Id);
        Assert.NotNull(entity.ApprovalExpiresAt);
        Assert.Single(pushService.Requests);
    }
    #endregion

    #region DEL-03: GetAsync
    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, _) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);
        InitiateDeliveryResponse initiated = await service.CreateAsync(new InitiateDeliveryRequest(locker.Code));

        GuestDeliveryStatusResponse res = await service.GetAsync(initiated.Id, initiated.GuestSessionToken);

        Assert.Equal(initiated.Id, res.RequestId);
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(Guid.NewGuid(), "TOKEN"));
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetAsync(Guid.Empty, ""));
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var tokenService = new Sha256TokenHashService();
        string rawToken = "token123";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeliveryRequest request = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(rawToken), DeliveryRequestStatus.PendingApproval, now.AddMinutes(15));
        request.ApprovalExpiresAt = now.AddSeconds(-1);
        db.DeliveryRequests.Add(request);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService);
        GuestDeliveryStatusResponse res = await service.GetAsync(request.Id, rawToken);

        Assert.Equal(DeliveryRequestStatus.Expired, res.Status);
        Assert.Equal(DeliveryRequestFailureCode.ApprovalExpired, res.FailureCode);
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        LockerCompartment comp = SeedCompartment(db, locker.Id, 1);
        var tokenService = new Sha256TokenHashService();
        string rawToken = "token123";
        DeliveryRequest request = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(rawToken), DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(15));
        request.AllocatedCompartmentId = comp.Id;
        Parcel parcel = new()
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = request.Id,
            ParcelCode = "P-123",
            Status = ParcelStatus.Stored
        };
        db.AddRange(request, parcel);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService);
        GuestDeliveryStatusResponse res = await service.GetAsync(request.Id, rawToken);

        Assert.Equal(comp.Code, res.CompartmentCode);
        Assert.Equal(parcel.Id, res.ParcelId);
    }
    #endregion

    #region DEL-04: ExpireStartedSessionsAsync
    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeliveryRequest started = CreateDeliveryRequest(locker.Id, policy.Id, "started", DeliveryRequestStatus.Started, now.AddMinutes(-5));
        db.DeliveryRequests.Add(started);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        int count = await service.ExpireStartedSessionsAsync();

        Assert.Equal(1, count);
        Assert.Equal(DeliveryRequestStatus.Expired, started.Status);
    }

    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequest started = CreateDeliveryRequest(locker.Id, policy.Id, "started", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(started);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        int count = await service.ExpireStartedSessionsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireStartedSessionsAsync();
        Assert.Equal(0, count);
    }
    #endregion

    #region DEL-05: GetPendingRequestsForResidentAsync
    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        PagedResult<PendingDeliveryRequestResponse> result = await service.GetPendingRequestsForResidentAsync(resident.UserId, default, 1, 10);

        Assert.Single(result.Items);
    }

    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");

        DeliveryRequestService service = CreateService(db);
        PagedResult<PendingDeliveryRequestResponse> result = await service.GetPendingRequestsForResidentAsync(resident.UserId, default, 1, 10);

        Assert.Empty(result.Items);
    }

    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetPendingRequestsForResidentAsync(Guid.Empty, default, 0, 0));
    }
    #endregion

    #region DEL-06: ApproveDeliveryRequestAsync
    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        DeliveryRequestSummaryResponse res = await service.ApproveDeliveryRequestAsync(resident.UserId, req.Id);

        Assert.Equal(DeliveryRequestStatus.Approved, res.Status);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequestService service = CreateService(db);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ApproveDeliveryRequestAsync(resident.UserId, Guid.NewGuid()));
        Assert.Equal("Không tìm thấy yêu cầu giao hàng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident1 = await SeedResidentAsync(db, "0911222333");
        ResidentProfile resident2 = await SeedResidentAsync(db, "0911222334");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident1.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveDeliveryRequestAsync(resident2.UserId, req.Id));
        Assert.Equal("Bạn không có quyền duyệt yêu cầu giao hàng này.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveDeliveryRequestAsync(resident.UserId, req.Id));
        Assert.Equal($"Không thể duyệt yêu cầu ở trạng thái {DeliveryRequestStatus.Started}.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => service.ApproveDeliveryRequestAsync(resident.UserId, req.Id));
        Assert.Equal("Thời hạn phê duyệt yêu cầu giao hàng đã hết.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.ApproveDeliveryRequestAsync(Guid.Empty, Guid.Empty));
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.ApprovalExpiresAt = null;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        DeliveryRequestSummaryResponse res = await service.ApproveDeliveryRequestAsync(resident.UserId, req.Id);

        Assert.Equal(DeliveryRequestStatus.Approved, res.Status);
    }
    #endregion

    #region DEL-07: RejectDeliveryRequestAsync
    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        DeliveryRequestSummaryResponse res = await service.RejectDeliveryRequestAsync(resident.UserId, req.Id);

        Assert.Equal(DeliveryRequestStatus.Rejected, res.Status);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequestService service = CreateService(db);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RejectDeliveryRequestAsync(resident.UserId, Guid.NewGuid()));
        Assert.Equal("Không tìm thấy yêu cầu giao hàng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident1 = await SeedResidentAsync(db, "0911222333");
        ResidentProfile resident2 = await SeedResidentAsync(db, "0911222334");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident1.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RejectDeliveryRequestAsync(resident2.UserId, req.Id));
        Assert.Equal("Bạn không có quyền từ chối yêu cầu giao hàng này.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RejectDeliveryRequestAsync(resident.UserId, req.Id));
        Assert.Equal($"Không thể từ chối yêu cầu ở trạng thái {DeliveryRequestStatus.Started}.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => service.RejectDeliveryRequestAsync(resident.UserId, req.Id));
        Assert.Equal("Thời hạn phê duyệt yêu cầu giao hàng đã hết.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.RejectDeliveryRequestAsync(Guid.Empty, Guid.Empty));
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.ApprovalExpiresAt = null;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        DeliveryRequestSummaryResponse res = await service.RejectDeliveryRequestAsync(resident.UserId, req.Id);

        Assert.Equal(DeliveryRequestStatus.Rejected, res.Status);
    }
    #endregion

    #region DEL-08: ExpirePendingApprovalsAsync
    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        int count = await service.ExpirePendingApprovalsAsync();

        Assert.Equal(1, count);
        Assert.Equal(DeliveryRequestStatus.Expired, req.Status);
    }

    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.PendingApproval, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ApprovalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        int count = await service.ExpirePendingApprovalsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpirePendingApprovalsAsync();
        Assert.Equal(0, count);
    }
    #endregion

    #region DEL-09: OpenCompartmentAsync
    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        LockerCompartment comp = SeedCompartment(db, locker.Id, 1);
        var tokenService = new Sha256TokenHashService();
        string rawToken = "valid-token";
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(rawToken), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService);
        CompartmentReservationResponse res = await service.OpenCompartmentAsync(req.Id, rawToken);

        Assert.Equal(comp.Id, res.CompartmentId);
        Assert.Equal(DeliveryRequestStatus.Allocated, req.Status);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var tokenService = new Sha256TokenHashService();
        string rawToken = "valid-token";
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(rawToken), DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(req.Id, rawToken));
        Assert.Equal($"Chưa thể phân bổ ngăn tủ cho yêu cầu ở trạng thái {DeliveryRequestStatus.Started}.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        // No compartments in locker
        var tokenService = new Sha256TokenHashService();
        string rawToken = "valid-token";
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(rawToken), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(req.Id, rawToken));
        Assert.Equal("Không tìm thấy ngăn tủ trống khả dụng tại locker này.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        var tokenService = new Sha256TokenHashService();
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken("token"), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService);
        await Assert.ThrowsAsync<AuthenticationException>(() => service.OpenCompartmentAsync(req.Id, "WRONG_TOKEN"));
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.OpenCompartmentAsync(Guid.Empty, ""));
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        SeedCompartment(db, locker.Id, 1);
        var tokenService = new Sha256TokenHashService();
        string rawToken = "valid-token";
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(rawToken), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService, lockerAccessService: new FailingLockerAccessService("Device offline"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(req.Id, rawToken));
        Assert.Equal("Device offline", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        SeedCompartment(db, locker.Id, 1);
        var tokenService = new Sha256TokenHashService();
        string rawToken = "valid-token";
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, tokenService.HashToken(rawToken), DeliveryRequestStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db, tokenHashService: tokenService, lockerAccessService: new FailingLockerAccessService(null));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenCompartmentAsync(req.Id, rawToken));
        Assert.Equal("Không thể mở ngăn locker.", ex.Message);
    }
    #endregion

    #region DEL-10: ExpireReservationsAsync
    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        LockerCompartment comp = SeedCompartment(db, locker.Id, 1);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(10));
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        int count = await service.ExpireReservationsAsync();

        Assert.Equal(1, count);
        Assert.Equal(DeliveryRequestStatus.Expired, req.Status);
    }

    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        LockerCompartment comp = SeedCompartment(db, locker.Id, 1);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(10));
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        int count = await service.ExpireReservationsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        int count = await service.ExpireReservationsAsync();
        Assert.Equal(0, count);
    }
    #endregion

    #region DEL-11: FinalizeDropOffAsync
    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC01()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        LockerCompartment comp = SeedCompartment(db, locker.Id, 1);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        await service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow);

        Assert.Equal(DeliveryRequestStatus.Deposited, req.Status);
        Assert.Single(db.Parcels);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC02()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.FinalizeDropOffAsync(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Equal("Không tìm thấy yêu cầu giao hàng.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC03()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Started, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow));
        Assert.Equal($"Không thể xác nhận gửi hàng khi yêu cầu ở trạng thái {DeliveryRequestStatus.Started}.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC04()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(10));
        req.AllocatedCompartmentId = null;
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow));
        Assert.Equal("Yêu cầu giao hàng chưa được phân bổ ngăn tủ.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC05()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        LockerCompartment comp = SeedCompartment(db, locker.Id, 1);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(10));
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow));
        Assert.Equal("Thời gian giữ ngăn tủ đã hết hạn.", ex.Message);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC06()
    {
        await using ApplicationDbContext db = CreateDbContext();
        DeliveryRequestService service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.FinalizeDropOffAsync(Guid.Empty, default));
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC07()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Deposited, DateTimeOffset.UtcNow.AddMinutes(10));
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        DeliveryRequestService service = CreateService(db);
        // Call when already Deposited (idempotent)
        await service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow);

        Assert.Equal(DeliveryRequestStatus.Deposited, req.Status);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC08()
    {
        await using ApplicationDbContext db = CreateDbContext();
        (Locker locker, SystemPolicy policy) = await SeedLockerAndPolicyAsync(db);
        LockerCompartment comp = SeedCompartment(db, locker.Id, 1);
        ResidentProfile resident = await SeedResidentAsync(db, "0911222333");
        DeliveryRequest req = CreateDeliveryRequest(locker.Id, policy.Id, "tok", DeliveryRequestStatus.Allocated, DateTimeOffset.UtcNow.AddMinutes(10));
        req.ResidentProfileId = resident.Id;
        req.AllocatedCompartmentId = comp.Id;
        req.ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        db.DeliveryRequests.Add(req);
        await db.SaveChangesAsync();

        var realtime = new RecordingRealtimeNotifier();
        DeliveryRequestService service = CreateService(db, realtimeNotifier: realtime);
        await service.FinalizeDropOffAsync(req.Id, DateTimeOffset.UtcNow);

        Assert.Single(realtime.Events);
        Assert.Equal("ParcelStored", realtime.Events[0].Event.Type);
    }
    #endregion

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DeliveryRequestService CreateService(
        ApplicationDbContext dbContext,
        Sha256TokenHashService? tokenHashService = null,
        IPushNotificationService? pushNotificationService = null,
        TimeProvider? timeProvider = null,
        ILockerAccessService? lockerAccessService = null,
        IOperationsRealtimeNotifier? realtimeNotifier = null) =>
        new(
            dbContext,
            tokenHashService ?? new Sha256TokenHashService(),
            pushNotificationService ?? new RecordingPushNotificationService(),
            lockerAccessService ?? new SuccessfulLockerAccessService(),
            new CompartmentAllocationService(dbContext, timeProvider ?? TimeProvider.System),
            timeProvider ?? TimeProvider.System,
            realtimeNotifier);

    private static async Task<(Locker Locker, SystemPolicy Policy)> SeedLockerAndPolicyAsync(ApplicationDbContext dbContext)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        User administrator = new()
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            PasswordHash = "hash",
            Role = UserRole.Administrator,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        Locker locker = new()
        {
            Id = Guid.NewGuid(),
            Code = "LOCKER-01",
            Address = "Address",
            RecoveryAddress = "Recovery address",
            DeviceIdentifier = "device-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = now,
            UpdatedAt = now
        };
        SystemPolicy policy = new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            DefaultApprovalMode = DeliveryApprovalMode.Manual,
            GuestSessionTimeoutMinutes = 15,
            ManualApprovalTimeoutMinutes = 10,
            CompartmentReservationMinutes = 10,
            OverdueStartAfterHours = 24,
            Currency = "VND",
            MaxStorageHours = 72,
            OtpMaxAttempts = 5,
            OtpLockoutMinutes = 15,
            EffectiveFrom = now.AddMinutes(-1),
            IsActive = true,
            CreatedByUserId = administrator.Id,
            CreatedAt = now
        };

        dbContext.AddRange(administrator, locker, policy);
        await dbContext.SaveChangesAsync();
        return (locker, policy);
    }

    private static async Task<ResidentProfile> SeedResidentAsync(
        ApplicationDbContext dbContext,
        string phoneNumber,
        DeliveryApprovalMode approvalMode = DeliveryApprovalMode.Auto)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid registeredLockerId = await dbContext.Lockers.Select(locker => locker.Id).SingleAsync();
        User user = new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            PasswordHash = "hash",
            Role = UserRole.Resident,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        ResidentProfile profile = new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RegisteredLockerId = registeredLockerId,
            FullName = "Cư Dân",
            DeliveryApprovalMode = approvalMode,
            User = user,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AddRange(user, profile);
        await dbContext.SaveChangesAsync();
        return profile;
    }

    private static LockerCompartment SeedCompartment(ApplicationDbContext dbContext, Guid lockerId, int number)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LockerCompartment compartment = new()
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            Code = $"C{number:D2}",
            HardwareCode = $"HW-C{number:D2}",
            HardwareChannel = number,
            OperationalStatus = LockerCompartmentOperationalStatus.Operational,
            DoorStatus = DoorStatus.Closed,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.LockerCompartments.Add(compartment);
        dbContext.SaveChanges();
        return compartment;
    }

    private static DeliveryRequest CreateDeliveryRequest(
        Guid lockerId,
        Guid policyId,
        string tokenHash,
        DeliveryRequestStatus status,
        DateTimeOffset sessionExpiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            LockerId = lockerId,
            SystemPolicyId = policyId,
            GuestSessionTokenHash = tokenHash,
            Status = status,
            LastActivityAt = DateTimeOffset.UtcNow,
            SessionExpiresAt = sessionExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private sealed class RecordingPushNotificationService : IPushNotificationService
    {
        public List<(Guid UserId, Guid RequestId, string LockerCode)> Requests { get; } = [];

        public Guid EnqueueDeliveryApprovalRequest(
            Guid residentUserId,
            Guid deliveryRequestId,
            string lockerCode)
        {
            Requests.Add((residentUserId, deliveryRequestId, lockerCode));
            return Guid.NewGuid();
        }

        public Guid EnqueueParcelStored(
            Guid residentUserId,
            Guid deliveryRequestId,
            Guid parcelId,
            string lockerCode,
            string compartmentCode) => Guid.NewGuid();

        public Guid EnqueueReturnNotification(
            Guid residentUserId,
            Guid returnRequestId,
            string type,
            string title,
            string message) => Guid.NewGuid();

        public Guid EnqueueParcelTransferred(
            Guid residentUserId,
            Guid deliveryRequestId,
            Guid parcelId,
            string collectionAddress) => Guid.NewGuid();

        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => Guid.NewGuid();

        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class SuccessfulLockerAccessService : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(
            OpenLockerRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new OpenLockerResponse(
                Guid.NewGuid(),
                request.LockerId,
                request.LockerCompartmentId,
                "simulator",
                1,
                LockerAccessResult.Succeeded,
                null,
                DateTimeOffset.UtcNow));
    }

    private sealed class FailingLockerAccessService(string? failureReason) : ILockerAccessService
    {
        public Task<OpenLockerResponse> OpenAsync(
            OpenLockerRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new OpenLockerResponse(
                Guid.NewGuid(),
                request.LockerId,
                request.LockerCompartmentId,
                "simulator",
                1,
                LockerAccessResult.Failed,
                failureReason,
                DateTimeOffset.UtcNow));
    }

    private sealed class RecordingRealtimeNotifier : IOperationsRealtimeNotifier
    {
        public List<(Guid LockerId, RealtimeEvent Event)> Events { get; } = [];

        public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default)
        {
            Events.Add((lockerId, message));
            return Task.CompletedTask;
        }

        public Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
