using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.DeliveryRequests;

public sealed class DeliveryRequestServiceTests
{
    private const string GuestToken = "valid-guest-token";
    private const string Phone = "0912345678";
    private const string ImageUrl = "https://example.com/parcel.jpg";
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    #region DEL-01: CreateAsync
    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC01()
    {
        await using TestContext context = await TestContext.CreateAsync(seedRequest: false);

        InitiateDeliveryResponse response = await context.Service.CreateAsync(new(context.Locker.Code));

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.NotEmpty(response.GuestSessionToken);
        Assert.Equal(DeliveryRequestStatus.Started, response.Status);
        Assert.Equal(Now.AddMinutes(15), response.SessionExpiresAt);
        context.Db.ChangeTracker.Clear();
        DeliveryRequest saved = Assert.Single(context.Db.DeliveryRequests);
        Assert.Equal(response.Id, saved.Id);
        Assert.Equal(context.Locker.Id, saved.LockerId);
        Assert.Equal(context.Policy.Id, saved.SystemPolicyId);
        Assert.Equal(new Sha256TokenHashService().HashToken(response.GuestSessionToken), saved.GuestSessionTokenHash);
        Assert.NotEqual(response.GuestSessionToken, saved.GuestSessionTokenHash);
        Assert.Equal(response.Status, saved.Status);
        Assert.Equal(response.SessionExpiresAt, saved.SessionExpiresAt);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(Now, saved.LastActivityAt);
        context.AssertAudit("DeliveryRequest.Created", saved.Id);
        context.AssertNoExternalCalls();
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC02()
    {
        await using TestContext context = await TestContext.CreateAsync(seedRequest: false);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => context.Service.CreateAsync(new("MISSING")));

        Assert.Equal("Locker với mã 'MISSING' không tồn tại.", exception.Message);
        context.AssertNoEffects();
        Assert.Empty(context.Db.DeliveryRequests);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC03()
    {
        await using TestContext context = await TestContext.CreateAsync(seedRequest: false);
        context.Locker.OperationalStatus = LockerOperationalStatus.OutOfService;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.CreateAsync(new(context.Locker.Code)));

        Assert.Equal("Locker hiện không sẵn sàng nhận hàng.", exception.Message);
        context.AssertNoEffects();
        Assert.Empty(context.Db.DeliveryRequests);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC04()
    {
        await using TestContext context = await TestContext.CreateAsync(seedRequest: false);
        context.Policy.IsActive = false;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.CreateAsync(new(context.Locker.Code)));

        Assert.Equal("Không có chính sách hệ thống đang hiệu lực.", exception.Message);
        context.AssertNoEffects();
        Assert.Empty(context.Db.DeliveryRequests);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC05()
    {
        await using TestContext context = await TestContext.CreateAsync(seedRequest: false);

        // The matrix does not specify an exception for null; this records the current service contract.
        await Assert.ThrowsAsync<NullReferenceException>(() => context.Service.CreateAsync(null!));

        context.AssertNoEffects();
        Assert.Empty(context.Db.DeliveryRequests);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC06()
    {
        await using TestContext context = await TestContext.CreateAsync(seedRequest: false);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => context.Service.CreateAsync(new("")));

        Assert.StartsWith("Giá trị không được để trống.", exception.Message);
        Assert.Equal("LockerCode", exception.ParamName);
        context.AssertNoEffects();
        Assert.Empty(context.Db.DeliveryRequests);
    }

    [Fact]
    [Trait("Category", "DEL-01")]
    public async Task CreateAsync_UTC07()
    {
        await using TestContext context = await TestContext.CreateAsync(seedRequest: false);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => context.Service.CreateAsync(new(new string('L', 51))));

        Assert.StartsWith("Giá trị không được vượt quá 50 ký tự.", exception.Message);
        Assert.Equal("LockerCode", exception.ParamName);
        context.AssertNoEffects();
        Assert.Empty(context.Db.DeliveryRequests);
    }
    #endregion

    #region DEL-02: SubmitAsync
    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC01()
    {
        await using TestContext context = await TestContext.CreateAsync();

        DeliveryRequestSummaryResponse response = await context.Service.SubmitAsync(context.Request.Id, GuestToken, new(ImageUrl, Phone));

        Assert.Equal(context.Request.Id, response.Id);
        Assert.Equal(ImageUrl, response.ParcelImageUrl);
        Assert.Equal(DeliveryRequestStatus.PendingApproval, response.Status);
        Assert.Equal(context.Request.SessionExpiresAt, response.SessionExpiresAt);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(context.Resident.Id, saved.ResidentProfileId);
        Assert.Equal(Phone, saved.RecipientPhoneSnapshot);
        Assert.Equal(ImageUrl, saved.ParcelImageUrl);
        Assert.Equal(DeliveryApprovalMode.Manual, saved.ApprovalModeSnapshot);
        Assert.Equal(response.Status, saved.Status);
        Assert.Equal(Now, saved.LastActivityAt);
        Assert.Equal(Now, saved.UpdatedAt);
        context.AssertAudit("DeliveryRequest.Submitted");
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC02()
    {
        await using TestContext context = await TestContext.CreateAsync();
        context.Resident.User.Status = UserStatus.Disabled;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => context.Service.SubmitAsync(context.Request.Id, GuestToken, new(ImageUrl, Phone)));

        Assert.Equal("Không tìm thấy Cư dân đang hoạt động với số điện thoại đã nhập.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC03()
    {
        await using TestContext context = await TestContext.CreateAsync();
        context.Resident.RegisteredLockerId = Guid.NewGuid();
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.SubmitAsync(context.Request.Id, GuestToken, new(ImageUrl, Phone)));

        Assert.Equal("Cư dân không đăng ký nhận hàng tại locker này.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC04()
    {
        await using TestContext context = await TestContext.CreateAsync();

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => context.Service.SubmitAsync(Guid.Empty, "", null!));

        Assert.Equal("Thiếu X-Guest-Session-Token.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC05()
    {
        await using TestContext context = await TestContext.CreateAsync();

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => context.Service.SubmitAsync(context.Request.Id, "", new(ImageUrl, Phone)));

        Assert.Equal("Thiếu X-Guest-Session-Token.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC06()
    {
        await using TestContext context = await TestContext.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => context.Service.SubmitAsync(context.Request.Id, GuestToken, new("relative/path.jpg", Phone)));

        Assert.StartsWith("ParcelImageUrl phải là URL HTTP hoặc HTTPS tuyệt đối.", exception.Message);
        Assert.Equal("value", exception.ParamName);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC07()
    {
        await using TestContext context = await TestContext.CreateAsync();
        context.Resident.DeliveryApprovalMode = DeliveryApprovalMode.Auto;
        await context.Db.SaveChangesAsync();

        DeliveryRequestSummaryResponse response = await context.Service.SubmitAsync(context.Request.Id, GuestToken, new(ImageUrl, Phone));

        Assert.Equal(DeliveryRequestStatus.Approved, response.Status);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Approved, saved.Status);
        Assert.Equal(Now, saved.DecisionAt);
        Assert.Null(saved.ApprovalExpiresAt);
        Assert.Equal(DeliveryApprovalMode.Auto, saved.ApprovalModeSnapshot);
        context.AssertNoExternalCalls();
        context.AssertAudit("DeliveryRequest.Submitted");
    }

    [Fact]
    [Trait("Category", "DEL-02")]
    public async Task SubmitAsync_UTC08()
    {
        await using TestContext context = await TestContext.CreateAsync();

        DeliveryRequestSummaryResponse response = await context.Service.SubmitAsync(context.Request.Id, GuestToken, new(ImageUrl, Phone));

        Assert.Equal(DeliveryRequestStatus.PendingApproval, response.Status);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(response.Status, saved.Status);
        Assert.Equal(Now.AddMinutes(10), saved.ApprovalExpiresAt);
        Assert.Null(saved.DecisionAt);
        Assert.Equal((context.Resident.UserId, context.Request.Id, context.Locker.Code), Assert.Single(context.Push.Approvals));
        Assert.Equal(context.Push.NotificationId, Assert.Single(context.Push.Sent));
        Assert.Empty(context.Push.Stored);
        Assert.Empty(context.Access.Calls);
        context.AssertAudit("DeliveryRequest.Submitted");
    }
    #endregion

    #region DEL-03: GetAsync
    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC01()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);

        GuestDeliveryStatusResponse response = await context.Service.GetAsync(context.Request.Id, GuestToken);

        Assert.Equal(context.Request.Id, response.RequestId);
        Assert.Equal(DeliveryRequestStatus.PendingApproval, response.Status);
        Assert.Equal(context.Request.ApprovalExpiresAt, response.ApprovalExpiresAt);
        Assert.Null(response.CompartmentCode);
        Assert.Null(response.ParcelId);
        Assert.Null(response.FailureCode);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.PendingApproval);
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC02()
    {
        await using TestContext context = await TestContext.CreateAsync();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => context.Service.GetAsync(Guid.NewGuid(), GuestToken));

        Assert.Equal("Không tìm thấy yêu cầu giao hàng.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC03()
    {
        await using TestContext context = await TestContext.CreateAsync();

        // The matrix expects success with an empty token; authentication runs before the ID lookup.
        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => context.Service.GetAsync(Guid.Empty, ""));

        Assert.Equal("Thiếu X-Guest-Session-Token.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC04()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);
        context.Request.ApprovalExpiresAt = Now;
        await context.Db.SaveChangesAsync();

        GuestDeliveryStatusResponse response = await context.Service.GetAsync(context.Request.Id, GuestToken);

        Assert.Equal(DeliveryRequestStatus.Expired, response.Status);
        Assert.Equal(DeliveryRequestFailureCode.ApprovalExpired, response.FailureCode);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(response.Status, saved.Status);
        Assert.Equal(response.FailureCode, saved.FailureCode);
        Assert.Equal(Now, saved.UpdatedAt);
        context.AssertNoEffects();
    }

    [Fact]
    [Trait("Category", "DEL-03")]
    public async Task GetAsync_UTC05()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Deposited);
        context.Request.AllocatedCompartmentId = context.Compartment.Id;
        context.Request.ReservationExpiresAt = Now.AddMinutes(10);
        var parcel = new Parcel { Id = Guid.NewGuid(), DeliveryRequestId = context.Request.Id, ParcelCode = "P-STORED", Status = ParcelStatus.Stored };
        context.Db.Parcels.Add(parcel);
        await context.Db.SaveChangesAsync();

        GuestDeliveryStatusResponse response = await context.Service.GetAsync(context.Request.Id, GuestToken);

        Assert.Equal(context.Compartment.Code, response.CompartmentCode);
        Assert.Equal(parcel.Id, response.ParcelId);
        Assert.Equal(context.Request.ReservationExpiresAt, response.ReservationExpiresAt);
        Assert.Equal(DeliveryRequestStatus.Deposited, response.Status);
        Assert.Single(context.Db.Parcels);
        Assert.Empty(context.Db.AuditLogs);
        context.AssertNoExternalCalls();
    }
    #endregion

    #region DEL-04: ExpireStartedSessionsAsync
    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC01()
    {
        await AssertExpirationAsync(DeliveryRequestStatus.Started, Now.AddSeconds(-1));
    }

    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC02()
    {
        await AssertNoExpirationAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-04")]
    public async Task ExpireStartedSessionsAsync_UTC03()
    {
        await AssertExpirationAsync(DeliveryRequestStatus.Started, Now);
    }
    #endregion

    #region DEL-05: GetPendingRequestsForResidentAsync
    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC01()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);
        context.Request.CreatedAt = Now;
        var eligible = new List<DeliveryRequest> { context.Request };
        for (int i = 1; i < 101; i++)
        {
            DeliveryRequest request = context.AddRequest(DeliveryRequestStatus.PendingApproval);
            request.CreatedAt = Now.AddMinutes(-i);
            eligible.Add(request);
        }
        context.Request.ApprovalExpiresAt = null; // Fallback to the snapshotted policy in the response.
        DeliveryRequest anotherResident = context.AddRequest(DeliveryRequestStatus.PendingApproval);
        anotherResident.ResidentProfileId = Guid.NewGuid();
        context.AddRequest(DeliveryRequestStatus.Approved);
        DeliveryRequest expired = context.AddRequest(DeliveryRequestStatus.PendingApproval);
        expired.ApprovalExpiresAt = Now;
        await context.Db.SaveChangesAsync();

        var response = await context.Service.GetPendingRequestsForResidentAsync(context.Resident.UserId, pageNumber: 10, pageSize: 10);

        Assert.Equal(101, response.TotalCount);
        Assert.Equal(10, response.PageNumber);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(eligible.Skip(90).Take(10).Select(r => r.Id), response.Items.Select(r => r.RequestId));
        Assert.All(response.Items, item =>
        {
            Assert.Equal(context.Locker.Code, item.LockerCode);
            Assert.Equal(context.Locker.Address, item.LockerAddress);
            Assert.Equal(ImageUrl, item.ParcelImageUrl);
            Assert.Equal(Phone, item.RecipientPhone);
        });
        var firstPage = await context.Service.GetPendingRequestsForResidentAsync(context.Resident.UserId);
        Assert.Equal(context.Request.Id, firstPage.Items[0].RequestId);
        Assert.Equal(context.Request.CreatedAt.AddMinutes(10), firstPage.Items[0].ApprovalExpiresAt);
        context.AssertNoEffects();
    }

    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC02()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);
        context.Resident.User.Status = UserStatus.Disabled;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => context.Service.GetPendingRequestsForResidentAsync(context.Resident.UserId, pageNumber: 10, pageSize: 10));

        Assert.Equal("Không tìm thấy hồ sơ Cư dân hợp lệ.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.PendingApproval);
    }

    [Fact]
    [Trait("Category", "DEL-05")]
    public async Task GetPendingRequestsForResidentAsync_UTC03()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);

        // A missing boundary user fails before paging; isolate zero paging with a valid resident too.
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            context.Service.GetPendingRequestsForResidentAsync(Guid.Empty, pageNumber: 0, pageSize: 0));
        Assert.Equal("Không tìm thấy hồ sơ Cư dân hợp lệ.", exception.Message);

        var response = await context.Service.GetPendingRequestsForResidentAsync(context.Resident.UserId, pageNumber: 0, pageSize: 0);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(1, response.PageSize);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(context.Request.Id, Assert.Single(response.Items).RequestId);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.PendingApproval);
    }
    #endregion

    #region DEL-06: ApproveDeliveryRequestAsync
    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC01()
    {
        await AssertDecisionAsync(approve: true, noDeadline: false);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC02()
    {
        await AssertMissingDecisionRequestAsync(approve: true);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC03()
    {
        await AssertUnauthorizedDecisionAsync(approve: true);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC04()
    {
        await AssertInvalidDecisionStatusAsync(approve: true);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC05()
    {
        await AssertExpiredDecisionAsync(approve: true);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC06()
    {
        await AssertEmptyDecisionIdsAsync(approve: true);
    }

    [Fact]
    [Trait("Category", "DEL-06")]
    public async Task ApproveDeliveryRequestAsync_UTC07()
    {
        await AssertDecisionAsync(approve: true, noDeadline: true);
    }
    #endregion

    #region DEL-07: RejectDeliveryRequestAsync
    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC01()
    {
        await AssertDecisionAsync(approve: false, noDeadline: false);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC02()
    {
        await AssertMissingDecisionRequestAsync(approve: false);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC03()
    {
        await AssertUnauthorizedDecisionAsync(approve: false);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC04()
    {
        await AssertInvalidDecisionStatusAsync(approve: false);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC05()
    {
        await AssertExpiredDecisionAsync(approve: false);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC06()
    {
        await AssertEmptyDecisionIdsAsync(approve: false);
    }

    [Fact]
    [Trait("Category", "DEL-07")]
    public async Task RejectDeliveryRequestAsync_UTC07()
    {
        await AssertDecisionAsync(approve: false, noDeadline: true);
    }
    #endregion

    #region DEL-08: ExpirePendingApprovalsAsync
    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC01()
    {
        await AssertExpirationAsync(DeliveryRequestStatus.PendingApproval, Now.AddSeconds(-1));
    }

    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC02()
    {
        await AssertNoExpirationAsync(DeliveryRequestStatus.PendingApproval);
    }

    [Fact]
    [Trait("Category", "DEL-08")]
    public async Task ExpirePendingApprovalsAsync_UTC03()
    {
        await AssertExpirationAsync(DeliveryRequestStatus.PendingApproval, Now);
    }
    #endregion

    #region DEL-09: OpenCompartmentAsync
    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC01()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Approved);

        CompartmentReservationResponse response = await context.Service.OpenCompartmentAsync(context.Request.Id, GuestToken);

        Assert.Equal(context.Request.Id, response.RequestId);
        Assert.Equal(context.Allocation.Reservation!.Id, response.ReservationId);
        Assert.Equal(context.Compartment.Id, response.CompartmentId);
        Assert.Equal(context.Compartment.Code, response.CompartmentCode);
        Assert.Equal(Now, response.ReservedAt);
        Assert.Equal(Now.AddMinutes(10), response.ReservationExpiresAt);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Allocated, saved.Status);
        Assert.Equal(context.Compartment.Id, saved.AllocatedCompartmentId);
        Assert.Equal(Now, saved.AllocatedAt);
        Assert.Equal(response.ReservationExpiresAt, saved.ReservationExpiresAt);
        Assert.Equal(Now, saved.LastActivityAt);
        Assert.Equal((context.Locker.Id, (Guid?)context.Request.Id, (Guid?)null, Now.AddMinutes(10)), Assert.Single(context.Allocation.Reserved));
        OpenLockerRequest open = Assert.Single(context.Access.Calls);
        Assert.Equal(context.Locker.Id, open.LockerId);
        Assert.Equal(context.Compartment.Id, open.LockerCompartmentId);
        Assert.Equal(context.Request.Id, open.DeliveryRequestId);
        Assert.Equal(LockerAccessType.ShipperDropOff, open.AccessType);
        Assert.Equal(LockerAccessMethod.GuestSession, open.AccessMethod);
        Assert.Null(open.UserId);
        Assert.Null(open.ReturnRequestId);
        Assert.Empty(context.Allocation.Released);
        context.AssertAudit("DeliveryRequest.CompartmentAllocated");
        Assert.Empty(context.Push.Sent);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC02()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Started);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.OpenCompartmentAsync(context.Request.Id, GuestToken));

        Assert.Equal("Chưa thể phân bổ ngăn tủ cho yêu cầu ở trạng thái Started.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Started);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC03()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Approved);
        context.Allocation.Reservation = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.OpenCompartmentAsync(context.Request.Id, GuestToken));

        Assert.Equal("Không tìm thấy ngăn tủ trống khả dụng tại locker này.", exception.Message);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Failed, saved.Status);
        Assert.Equal(DeliveryRequestFailureCode.NoCompartment, saved.FailureCode);
        Assert.Equal("Không có ngăn locker trống khả dụng.", saved.FailureDetail);
        Assert.Equal(Now, saved.UpdatedAt);
        Assert.Null(saved.AllocatedCompartmentId);
        Assert.Single(context.Allocation.Reserved);
        Assert.Empty(context.Allocation.Released);
        Assert.Empty(context.Access.Calls);
        Assert.Empty(context.Db.AuditLogs);
        Assert.Empty(context.Push.Sent);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC04()
    {
        await AssertOpenFailureAsync(null, LockerAccessResult.Blocked);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC05()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Approved);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => context.Service.OpenCompartmentAsync(Guid.Empty, ""));

        Assert.Equal("Thiếu X-Guest-Session-Token.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Approved);
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC06()
    {
        await AssertOpenFailureAsync("Device offline");
    }

    [Fact]
    [Trait("Category", "DEL-09")]
    public async Task OpenCompartmentAsync_UTC07()
    {
        await AssertOpenFailureAsync(null);
    }
    #endregion

    #region DEL-10: ExpireReservationsAsync
    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC01()
    {
        await AssertExpirationAsync(DeliveryRequestStatus.Allocated, Now.AddSeconds(-1));
    }

    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC02()
    {
        await AssertNoExpirationAsync(DeliveryRequestStatus.Allocated);
    }

    [Fact]
    [Trait("Category", "DEL-10")]
    public async Task ExpireReservationsAsync_UTC03()
    {
        await AssertExpirationAsync(DeliveryRequestStatus.Allocated, Now);
    }
    #endregion

    #region DEL-11: FinalizeDropOffAsync
    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC01()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Allocated);

        // Completion uses the supplied event time, independently of TimeProvider.GetUtcNow().
        await context.Service.FinalizeDropOffAsync(context.Request.Id, Now.AddMinutes(1));

        await AssertDepositedAsync(context, Now.AddMinutes(1));
        Assert.Empty(context.Realtime.LockerEvents);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC02()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Allocated);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => context.Service.FinalizeDropOffAsync(Guid.NewGuid(), Now));

        Assert.Equal("Không tìm thấy yêu cầu giao hàng.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Allocated);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC03()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Approved);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.FinalizeDropOffAsync(context.Request.Id, Now));

        Assert.Equal("Không thể xác nhận gửi hàng khi yêu cầu ở trạng thái Approved.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Approved);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC04()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Allocated);
        context.Request.AllocatedCompartmentId = null;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.FinalizeDropOffAsync(context.Request.Id, Now));

        Assert.Equal("Yêu cầu giao hàng chưa được phân bổ ngăn tủ.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Allocated);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC05()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Allocated);
        context.Request.ReservationExpiresAt = Now;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => context.Service.FinalizeDropOffAsync(context.Request.Id, Now));

        Assert.Equal("Thời gian giữ ngăn tủ đã hết hạn.", exception.Message);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Expired, saved.Status);
        Assert.Equal(DeliveryRequestFailureCode.ReservationExpired, saved.FailureCode);
        Assert.Equal(Now, saved.UpdatedAt);
        Assert.Null(saved.DepositedAt);
        context.AssertNoEffects();
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC06()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Allocated);

        // DateTimeOffset is non-nullable: use default for the matrix's null completion boundary.
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => context.Service.FinalizeDropOffAsync(Guid.Empty, default));

        Assert.Equal("Không tìm thấy yêu cầu giao hàng.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Allocated);
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC07()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Allocated, realtime: true);
        await context.Service.FinalizeDropOffAsync(context.Request.Id, Now);
        context.Db.ChangeTracker.Clear();
        Parcel parcel = Assert.Single(context.Db.Parcels);
        Guid historyId = Assert.Single(context.Db.ParcelStatusHistories).Id;
        Guid auditId = Assert.Single(context.Db.AuditLogs).Id;
        context.ResetCalls();

        await context.Service.FinalizeDropOffAsync(context.Request.Id, Now.AddMinutes(2));

        Assert.Equal(parcel.Id, Assert.Single(context.Db.Parcels).Id);
        Assert.Equal(historyId, Assert.Single(context.Db.ParcelStatusHistories).Id);
        Assert.Equal(auditId, Assert.Single(context.Db.AuditLogs).Id);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Deposited, saved.Status);
        Assert.Equal(Now, saved.DepositedAt);
        Assert.Equal(Now, saved.UpdatedAt);
        context.AssertNoExternalCalls();
    }

    [Fact]
    [Trait("Category", "DEL-11")]
    public async Task FinalizeDropOffAsync_UTC08()
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Allocated, realtime: true);

        await context.Service.FinalizeDropOffAsync(context.Request.Id, Now);

        await AssertDepositedAsync(context, Now);
        (Guid lockerId, RealtimeEvent message) = Assert.Single(context.Realtime.LockerEvents);
        Assert.Equal(context.Locker.Id, lockerId);
        Assert.Equal("ParcelStored", message.Type);
        Assert.Equal(Assert.Single(context.Db.Parcels).Id, message.EntityId);
        Assert.Equal(context.Locker.Id, message.LockerId);
        Assert.Equal("Stored", message.Status);
        Assert.Equal(Now, message.OccurredAt);
        Assert.Empty(context.Realtime.UserEvents);
    }
    #endregion

    #region Shared assertions
    private static Task<DeliveryRequestSummaryResponse> DecideAsync(TestContext context, bool approve, Guid userId, Guid requestId) =>
        approve
            ? context.Service.ApproveDeliveryRequestAsync(userId, requestId)
            : context.Service.RejectDeliveryRequestAsync(userId, requestId);

    private static async Task AssertDecisionAsync(bool approve, bool noDeadline)
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);
        if (noDeadline)
        {
            context.Request.ApprovalExpiresAt = null;
            await context.Db.SaveChangesAsync();
        }

        DeliveryRequestSummaryResponse response = await DecideAsync(context, approve, context.Resident.UserId, context.Request.Id);

        DeliveryRequestStatus expected = approve ? DeliveryRequestStatus.Approved : DeliveryRequestStatus.Rejected;
        Assert.Equal(context.Request.Id, response.Id);
        Assert.Equal(expected, response.Status);
        Assert.Equal(ImageUrl, response.ParcelImageUrl);
        Assert.Equal(context.Request.SessionExpiresAt, response.SessionExpiresAt);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(expected, saved.Status);
        Assert.Equal(Now, saved.DecisionAt);
        Assert.Equal(Now, saved.LastActivityAt);
        Assert.Equal(Now, saved.UpdatedAt);
        Assert.Null(saved.FailureCode);
        if (noDeadline)
        {
            Assert.Null(saved.ApprovalExpiresAt);
        }
        context.AssertAudit(approve ? "DeliveryRequest.Approved" : "DeliveryRequest.Rejected", actorUserId: context.Resident.UserId);
        context.AssertNoExternalCalls();
        Assert.Empty(context.Db.Parcels);
    }

    private static async Task AssertMissingDecisionRequestAsync(bool approve)
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => DecideAsync(context, approve, context.Resident.UserId, Guid.NewGuid()));

        Assert.Equal("Không tìm thấy yêu cầu giao hàng.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.PendingApproval);
    }

    private static async Task AssertUnauthorizedDecisionAsync(bool approve)
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);
        context.Request.ResidentProfileId = Guid.NewGuid();
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => DecideAsync(context, approve, context.Resident.UserId, context.Request.Id));

        Assert.Equal(approve ? "Bạn không có quyền duyệt yêu cầu giao hàng này." : "Bạn không có quyền từ chối yêu cầu giao hàng này.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.PendingApproval);
    }

    private static async Task AssertInvalidDecisionStatusAsync(bool approve)
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Approved);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DecideAsync(context, approve, context.Resident.UserId, context.Request.Id));

        Assert.Equal(approve ? "Không thể duyệt yêu cầu ở trạng thái Approved." : "Không thể từ chối yêu cầu ở trạng thái Approved.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.Approved);
    }

    private static async Task AssertExpiredDecisionAsync(bool approve)
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);
        context.Request.ApprovalExpiresAt = Now;
        await context.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => DecideAsync(context, approve, context.Resident.UserId, context.Request.Id));

        Assert.Equal("Thời hạn phê duyệt yêu cầu giao hàng đã hết.", exception.Message);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Expired, saved.Status);
        Assert.Equal(DeliveryRequestFailureCode.ApprovalExpired, saved.FailureCode);
        Assert.Equal(Now, saved.UpdatedAt);
        Assert.Null(saved.DecisionAt);
        context.AssertNoEffects();
    }

    private static async Task AssertEmptyDecisionIdsAsync(bool approve)
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.PendingApproval);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => DecideAsync(context, approve, Guid.Empty, Guid.Empty));

        Assert.Equal("Không tìm thấy hồ sơ Cư dân hợp lệ.", exception.Message);
        await context.AssertUnchangedAsync(DeliveryRequestStatus.PendingApproval);
    }

    private static Task<int> ExpireAsync(TestContext context, DeliveryRequestStatus status) => status switch
    {
        DeliveryRequestStatus.Started => context.Service.ExpireStartedSessionsAsync(),
        DeliveryRequestStatus.PendingApproval => context.Service.ExpirePendingApprovalsAsync(),
        DeliveryRequestStatus.Allocated => context.Service.ExpireReservationsAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static void SetDeadline(DeliveryRequest request, DeliveryRequestStatus status, DateTimeOffset? deadline)
    {
        switch (status)
        {
            case DeliveryRequestStatus.Started:
                request.SessionExpiresAt = deadline!.Value;
                break;
            case DeliveryRequestStatus.PendingApproval:
                request.ApprovalExpiresAt = deadline;
                break;
            case DeliveryRequestStatus.Allocated:
                request.ReservationExpiresAt = deadline;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static async Task AssertExpirationAsync(DeliveryRequestStatus status, DateTimeOffset deadline)
    {
        await using TestContext context = await TestContext.CreateAsync(status);
        SetDeadline(context.Request, status, deadline);
        DeliveryRequest secondExpired = context.AddRequest(status);
        SetDeadline(secondExpired, status, Now.AddMinutes(-2));
        DeliveryRequest future = context.AddRequest(status);
        SetDeadline(future, status, Now.AddTicks(1));
        DeliveryRequest differentStatus = context.AddRequest(DeliveryRequestStatus.Deposited);
        SetDeadline(differentStatus, status, Now.AddMinutes(-1));
        DeliveryRequest? noDeadline = null;
        if (status != DeliveryRequestStatus.Started)
        {
            noDeadline = context.AddRequest(status);
            SetDeadline(noDeadline, status, null);
        }
        await context.Db.SaveChangesAsync();

        int count = await ExpireAsync(context, status);

        Assert.Equal(2, count);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        DeliveryRequestFailureCode failure = status switch
        {
            DeliveryRequestStatus.Started => DeliveryRequestFailureCode.SessionExpired,
            DeliveryRequestStatus.PendingApproval => DeliveryRequestFailureCode.ApprovalExpired,
            _ => DeliveryRequestFailureCode.ReservationExpired
        };
        Assert.Equal(DeliveryRequestStatus.Expired, saved.Status);
        Assert.Equal(failure, saved.FailureCode);
        Assert.Equal(Now, saved.UpdatedAt);
        DeliveryRequest secondSaved = await context.Db.DeliveryRequests.SingleAsync(r => r.Id == secondExpired.Id);
        Assert.Equal(DeliveryRequestStatus.Expired, secondSaved.Status);
        Assert.Equal(failure, secondSaved.FailureCode);
        Assert.Equal(status, (await context.Db.DeliveryRequests.SingleAsync(r => r.Id == future.Id)).Status);
        Assert.Equal(DeliveryRequestStatus.Deposited, (await context.Db.DeliveryRequests.SingleAsync(r => r.Id == differentStatus.Id)).Status);
        if (noDeadline is not null)
        {
            Assert.Equal(status, (await context.Db.DeliveryRequests.SingleAsync(r => r.Id == noDeadline.Id)).Status);
        }
        Assert.Equal(2, context.Db.AuditLogs.Count());
        Assert.All(context.Db.AuditLogs, audit =>
        {
            Assert.Equal("DeliveryRequest.Expired", audit.Action);
            Assert.Equal(AuditLogResult.Succeeded, audit.Result);
            Assert.Equal(Now, audit.OccurredAt);
            Assert.Contains(audit.EntityId, new Guid?[] { context.Request.Id, secondExpired.Id });
        });
        Assert.Empty(context.Allocation.Reserved);
        Assert.Empty(context.Access.Calls);
        Assert.Empty(context.Push.Sent);
        if (status == DeliveryRequestStatus.Allocated)
        {
            Assert.Equal(2, context.Allocation.Released.Count);
            Assert.Contains((context.Request.Id, (Guid?)null, Now), context.Allocation.Released);
            Assert.Contains((secondExpired.Id, (Guid?)null, Now), context.Allocation.Released);
        }
        else
        {
            Assert.Empty(context.Allocation.Released);
        }
    }

    private static async Task AssertNoExpirationAsync(DeliveryRequestStatus status)
    {
        await using TestContext context = await TestContext.CreateAsync(status);
        SetDeadline(context.Request, status, Now.AddTicks(1));
        DeliveryRequest other = context.AddRequest(DeliveryRequestStatus.Deposited);
        SetDeadline(other, status, Now.AddMinutes(-1));
        await context.Db.SaveChangesAsync();

        int count = await ExpireAsync(context, status);

        Assert.Equal(0, count);
        await context.AssertUnchangedAsync(status);
        Assert.Equal(DeliveryRequestStatus.Deposited, (await context.Db.DeliveryRequests.SingleAsync(r => r.Id == other.Id)).Status);
    }

    private static async Task AssertOpenFailureAsync(string? reason, LockerAccessResult result = LockerAccessResult.Failed)
    {
        await using TestContext context = await TestContext.CreateAsync(DeliveryRequestStatus.Approved);
        context.Access.Result = result;
        context.Access.FailureReason = reason;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.OpenCompartmentAsync(context.Request.Id, GuestToken));

        Assert.Equal(reason ?? "Không thể mở ngăn locker.", exception.Message);
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Failed, saved.Status);
        Assert.Equal(DeliveryRequestFailureCode.DeviceUnavailable, saved.FailureCode);
        Assert.Equal(reason, saved.FailureDetail);
        Assert.Equal(Now, saved.UpdatedAt);
        Assert.Single(context.Allocation.Reserved);
        Assert.Single(context.Access.Calls);
        Assert.Equal(((Guid?)context.Request.Id, (Guid?)null, Now), Assert.Single(context.Allocation.Released));
        // Allocation is persisted and audited before the device call, even if opening fails.
        context.AssertAudit("DeliveryRequest.CompartmentAllocated");
        Assert.Empty(context.Db.Parcels);
        Assert.Empty(context.Push.Sent);
    }

    private static async Task AssertDepositedAsync(TestContext context, DateTimeOffset completedAt)
    {
        DeliveryRequest saved = await context.ReloadRequestAsync();
        Assert.Equal(DeliveryRequestStatus.Deposited, saved.Status);
        Assert.Equal(completedAt, saved.DepositedAt);
        Assert.Equal(completedAt, saved.CompartmentReleasedAt);
        Assert.Equal(completedAt, saved.LastActivityAt);
        Assert.Equal(completedAt, saved.UpdatedAt);
        Parcel parcel = Assert.Single(context.Db.Parcels);
        Assert.Equal(saved.Id, parcel.DeliveryRequestId);
        Assert.Equal(ParcelStatus.Stored, parcel.Status);
        Assert.NotEmpty(parcel.ParcelCode);
        Assert.Equal(completedAt, parcel.StoredAt);
        // 2026-10-15 00:00 in Vietnam is the first counting day after this completion.
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 17, 0, 0, TimeSpan.Zero), parcel.PickupDueAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 17, 0, 0, TimeSpan.Zero), parcel.MaxStorageUntil);
        ParcelStatusHistory history = Assert.Single(context.Db.ParcelStatusHistories);
        Assert.Equal(parcel.Id, history.ParcelId);
        Assert.Null(history.FromStatus);
        Assert.Equal(ParcelStatus.Stored, history.ToStatus);
        Assert.Equal(completedAt, history.ChangedAt);
        Assert.Equal(((Guid?)saved.Id, (Guid?)null, completedAt), Assert.Single(context.Allocation.Released));
        Assert.Equal((context.Resident.UserId, saved.Id, parcel.Id, context.Locker.Code, context.Compartment.Code), Assert.Single(context.Push.Stored));
        Assert.Equal(context.Push.NotificationId, Assert.Single(context.Push.Sent));
        Assert.Empty(context.Push.Approvals);
        Assert.Empty(context.Allocation.Reserved);
        Assert.Empty(context.Access.Calls);
        context.AssertAudit("DeliveryRequest.Deposited", occurredAt: completedAt);
        Assert.Equal($"ParcelId={parcel.Id}", Assert.Single(context.Db.AuditLogs).Details);
    }
    #endregion

    #region Test infrastructure
    private sealed class TestContext : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Locker Locker { get; } = new()
        {
            Id = Guid.NewGuid(),
            Code = "LOCKER-01",
            Address = "123 Nguyen Van Linh",
            RecoveryAddress = "456 Nguyen Van Linh",
            DeviceIdentifier = "DEVICE-01",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online,
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1)
        };
        public SystemPolicy Policy { get; } = new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            IsActive = true,
            EffectiveFrom = Now.AddDays(-1),
            GuestSessionTimeoutMinutes = 15,
            ManualApprovalTimeoutMinutes = 10,
            CompartmentReservationMinutes = 10,
            OverdueStartAfterHours = 24,
            MaxStorageHours = 72,
            Currency = "VND",
            CreatedAt = Now.AddDays(-1)
        };
        public ResidentProfile Resident { get; private set; } = null!;
        public LockerCompartment Compartment { get; private set; } = null!;
        public DeliveryRequest Request { get; private set; } = null!;
        public RecordingPushService Push { get; } = new();
        public RecordingLockerAccess Access { get; } = new();
        public RecordingAllocation Allocation { get; } = new();
        public RecordingRealtime Realtime { get; } = new();
        public DeliveryRequestService Service { get; private set; } = null!;

        public static async Task<TestContext> CreateAsync(DeliveryRequestStatus status = DeliveryRequestStatus.Started,
            bool seedRequest = true, bool realtime = false)
        {
            var context = new TestContext();
            var admin = new User { Id = Guid.NewGuid(), PasswordHash = "hash", Role = UserRole.Administrator, Status = UserStatus.Active };
            context.Policy.CreatedByUserId = admin.Id;
            context.Policy.CreatedByUser = admin;
            var user = new User { Id = Guid.NewGuid(), PhoneNumber = Phone, PasswordHash = "hash", Role = UserRole.Resident, Status = UserStatus.Active };
            context.Resident = new ResidentProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                FullName = "Cu Dan",
                RegisteredLockerId = context.Locker.Id,
                DeliveryApprovalMode = DeliveryApprovalMode.Manual
            };
            context.Compartment = new LockerCompartment
            {
                Id = Guid.NewGuid(),
                LockerId = context.Locker.Id,
                Code = "C01",
                HardwareCode = "HW-C01",
                HardwareChannel = 1,
                OperationalStatus = LockerCompartmentOperationalStatus.Operational,
                DoorStatus = DoorStatus.Closed
            };
            context.Db.AddRange(admin, context.Locker, context.Policy, user, context.Resident, context.Compartment);
            if (seedRequest)
            {
                context.Request = context.AddRequest(status);
            }
            context.Allocation.Reservation = new CompartmentReservation
            {
                Id = Guid.NewGuid(),
                LockerCompartmentId = context.Compartment.Id,
                LockerCompartment = context.Compartment,
                DeliveryRequestId = seedRequest ? context.Request.Id : null,
                ReservedAt = Now,
                ExpiresAt = Now.AddMinutes(10)
            };
            await context.Db.SaveChangesAsync();
            context.Service = new DeliveryRequestService(context.Db, new Sha256TokenHashService(), context.Push,
                context.Access, context.Allocation, new FixedTimeProvider(), realtime ? context.Realtime : null);
            return context;
        }

        public DeliveryRequest AddRequest(DeliveryRequestStatus status)
        {
            var request = new DeliveryRequest
            {
                Id = Guid.NewGuid(),
                LockerId = Locker.Id,
                SystemPolicyId = Policy.Id,
                GuestSessionTokenHash = new Sha256TokenHashService().HashToken(GuestToken),
                Status = status,
                ResidentProfileId = status == DeliveryRequestStatus.Started ? null : Resident.Id,
                ParcelImageUrl = status == DeliveryRequestStatus.Started ? null : ImageUrl,
                RecipientPhoneSnapshot = status == DeliveryRequestStatus.Started ? null : Phone,
                SessionExpiresAt = Now.AddMinutes(15),
                ApprovalExpiresAt = status == DeliveryRequestStatus.PendingApproval ? Now.AddMinutes(10) : null,
                AllocatedCompartmentId = status == DeliveryRequestStatus.Allocated ? Compartment.Id : null,
                ReservationExpiresAt = status == DeliveryRequestStatus.Allocated ? Now.AddMinutes(10) : null,
                CreatedAt = Now.AddMinutes(-5),
                UpdatedAt = Now.AddMinutes(-5),
                LastActivityAt = Now.AddMinutes(-5)
            };
            Db.DeliveryRequests.Add(request);
            return request;
        }

        public async Task<DeliveryRequest> ReloadRequestAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.DeliveryRequests.SingleAsync(r => r.Id == Request.Id);
        }

        public async Task AssertUnchangedAsync(DeliveryRequestStatus status)
        {
            DeliveryRequest saved = await ReloadRequestAsync();
            Assert.Equal(status, saved.Status);
            Assert.Equal(Now.AddMinutes(-5), saved.UpdatedAt);
            Assert.Null(saved.DecisionAt);
            Assert.Null(saved.DepositedAt);
            Assert.Null(saved.FailureCode);
            if (status == DeliveryRequestStatus.Started)
            {
                Assert.Null(saved.ResidentProfileId);
                Assert.Null(saved.ParcelImageUrl);
            }
            AssertNoEffects();
        }

        public void AssertAudit(string action, Guid? requestId = null, Guid? actorUserId = null, DateTimeOffset? occurredAt = null)
        {
            AuditLog audit = Assert.Single(Db.AuditLogs);
            Assert.Equal(action, audit.Action);
            Assert.Equal(nameof(DeliveryRequest), audit.EntityType);
            Assert.Equal(requestId ?? Request.Id, audit.EntityId);
            Assert.Equal(actorUserId, audit.ActorUserId);
            Assert.Equal(AuditLogResult.Succeeded, audit.Result);
            Assert.Equal(occurredAt ?? Now, audit.OccurredAt);
        }

        public void AssertNoEffects()
        {
            Assert.Empty(Db.AuditLogs);
            Assert.Empty(Db.Parcels);
            Assert.Empty(Db.ParcelStatusHistories);
            AssertNoExternalCalls();
        }

        public void AssertNoExternalCalls()
        {
            Assert.Empty(Push.Approvals);
            Assert.Empty(Push.Stored);
            Assert.Empty(Push.Sent);
            Assert.Empty(Access.Calls);
            Assert.Empty(Allocation.Reserved);
            Assert.Empty(Allocation.Released);
            Assert.Empty(Realtime.LockerEvents);
            Assert.Empty(Realtime.UserEvents);
        }

        public void ResetCalls()
        {
            Push.Approvals.Clear();
            Push.Stored.Clear();
            Push.Sent.Clear();
            Access.Calls.Clear();
            Allocation.Reserved.Clear();
            Allocation.Released.Clear();
            Realtime.LockerEvents.Clear();
            Realtime.UserEvents.Clear();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingPushService : IPushNotificationService
    {
        public Guid NotificationId { get; } = Guid.NewGuid();
        public List<(Guid UserId, Guid RequestId, string LockerCode)> Approvals { get; } = [];
        public List<(Guid UserId, Guid RequestId, Guid ParcelId, string LockerCode, string CompartmentCode)> Stored { get; } = [];
        public List<Guid> Sent { get; } = [];

        public Guid EnqueueDeliveryApprovalRequest(Guid residentUserId, Guid deliveryRequestId, string lockerCode)
        {
            Approvals.Add((residentUserId, deliveryRequestId, lockerCode));
            return NotificationId;
        }

        public Guid EnqueueParcelStored(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, string compartmentCode)
        {
            Stored.Add((residentUserId, deliveryRequestId, parcelId, lockerCode, compartmentCode));
            return NotificationId;
        }

        public Task TrySendAsync(Guid notificationId, CancellationToken cancellationToken = default)
        {
            Sent.Add(notificationId);
            return Task.CompletedTask;
        }

        public Guid EnqueueReturnNotification(Guid residentUserId, Guid returnRequestId, string type, string title, string message) => throw new NotSupportedException();
        public Guid EnqueueParcelTransferred(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string collectionAddress) => throw new NotSupportedException();
        public Guid EnqueueParcelPickupReminder(Guid residentUserId, Guid deliveryRequestId, Guid parcelId, string lockerCode, int daysUntilTransfer, bool isOverdue) => throw new NotSupportedException();
        public Task<int> RetryPendingDeliveryApprovalNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingLockerAccess : ILockerAccessService
    {
        public LockerAccessResult Result { get; set; } = LockerAccessResult.Succeeded;
        public string? FailureReason { get; set; }
        public List<OpenLockerRequest> Calls { get; } = [];

        public Task<OpenLockerResponse> OpenAsync(OpenLockerRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(new OpenLockerResponse(Guid.NewGuid(), request.LockerId, request.LockerCompartmentId,
                "DEVICE-01", 1, Result, FailureReason, Now));
        }
    }

    private sealed class RecordingAllocation : ICompartmentAllocationService
    {
        public CompartmentReservation? Reservation { get; set; }
        public List<(Guid LockerId, Guid? DeliveryId, Guid? ReturnId, DateTimeOffset ExpiresAt)> Reserved { get; } = [];
        public List<(Guid? DeliveryId, Guid? ReturnId, DateTimeOffset ReleasedAt)> Released { get; } = [];

        public Task<CompartmentReservation?> ReserveAvailableAsync(Guid lockerId, Guid? deliveryRequestId, Guid? returnRequestId,
            DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            Reserved.Add((lockerId, deliveryRequestId, returnRequestId, expiresAt));
            return Task.FromResult(Reservation);
        }

        public Task ReleaseAsync(Guid? deliveryRequestId, Guid? returnRequestId, DateTimeOffset releasedAt, CancellationToken cancellationToken = default)
        {
            Released.Add((deliveryRequestId, returnRequestId, releasedAt));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRealtime : IOperationsRealtimeNotifier
    {
        public List<(Guid LockerId, RealtimeEvent Message)> LockerEvents { get; } = [];
        public List<(Guid UserId, RealtimeEvent Message)> UserEvents { get; } = [];

        public Task PublishToLockerAsync(Guid lockerId, RealtimeEvent message, CancellationToken cancellationToken = default)
        {
            LockerEvents.Add((lockerId, message));
            return Task.CompletedTask;
        }

        public Task PublishToUserAsync(Guid userId, RealtimeEvent message, CancellationToken cancellationToken = default)
        {
            UserEvents.Add((userId, message));
            return Task.CompletedTask;
        }
    }
    #endregion
}
