using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class DeliveryRequestService(
    ApplicationDbContext dbContext,
    ITokenHashService tokenHashService,
    IPushNotificationService pushNotificationService,
    ILockerAccessService lockerAccessService,
    ICompartmentAllocationService compartmentAllocationService,
    TimeProvider timeProvider) : IDeliveryRequestService
{
    public async Task<InitiateDeliveryResponse> CreateAsync(
        InitiateDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        string lockerCode = RequireValue(request.LockerCode, nameof(request.LockerCode), 50);
        DateTimeOffset now = timeProvider.GetUtcNow();

        Locker locker = await dbContext.Lockers.SingleOrDefaultAsync(
            item => item.Code == lockerCode,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Locker với mã '{lockerCode}' không tồn tại.");

        if (locker.OperationalStatus != LockerOperationalStatus.Operational)
        {
            throw new InvalidOperationException("Locker hiện không sẵn sàng nhận hàng.");
        }

        SystemPolicy policy = await dbContext.SystemPolicies.SingleOrDefaultAsync(
            item => item.IsActive &&
                    item.EffectiveFrom <= now &&
                    (item.EffectiveTo == null || item.EffectiveTo > now),
            cancellationToken)
            ?? throw new InvalidOperationException("Không có chính sách hệ thống đang hiệu lực.");

        string guestSessionToken = tokenHashService.CreateSecureToken();
        DeliveryRequest deliveryRequest = new()
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            SystemPolicyId = policy.Id,
            GuestSessionTokenHash = tokenHashService.HashToken(guestSessionToken),
            Status = DeliveryRequestStatus.Started,
            LastActivityAt = now,
            SessionExpiresAt = now.AddMinutes(policy.GuestSessionTimeoutMinutes),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.DeliveryRequests.Add(deliveryRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new InitiateDeliveryResponse(
            deliveryRequest.Id,
            guestSessionToken,
            deliveryRequest.Status,
            deliveryRequest.SessionExpiresAt);
    }

    public async Task<DeliveryRequestSummaryResponse> SubmitAsync(
        Guid id,
        string guestSessionToken,
        SubmitDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);
        string recipientPhone = RequireValue(request.RecipientPhone, nameof(request.RecipientPhone), 20);
        string parcelImageUrl = ValidateImageUrl(request.ParcelImageUrl);
        DateTimeOffset now = timeProvider.GetUtcNow();

        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(profile => profile.User)
            .SingleOrDefaultAsync(
                profile => profile.User.PhoneNumber == recipientPhone &&
                           profile.User.Role == UserRole.Resident &&
                           profile.User.Status == UserStatus.Active,
                cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy Cư dân đang hoạt động với số điện thoại đã nhập.");

        if (resident.RegisteredLockerId != deliveryRequest.LockerId)
        {
            throw new InvalidOperationException("Cư dân không đăng ký nhận hàng tại locker này.");
        }

        deliveryRequest.ResidentProfileId = resident.Id;
        deliveryRequest.ParcelImageUrl = parcelImageUrl;
        deliveryRequest.RecipientPhoneSnapshot = recipientPhone;
        deliveryRequest.ApprovalModeSnapshot = resident.DeliveryApprovalMode;
        deliveryRequest.Status = resident.DeliveryApprovalMode == DeliveryApprovalMode.Auto
            ? DeliveryRequestStatus.Approved
            : DeliveryRequestStatus.PendingApproval;
        deliveryRequest.ApprovalExpiresAt = resident.DeliveryApprovalMode == DeliveryApprovalMode.Manual
            ? now.AddMinutes(deliveryRequest.SystemPolicy.ManualApprovalTimeoutMinutes)
            : null;
        deliveryRequest.DecisionAt = resident.DeliveryApprovalMode == DeliveryApprovalMode.Auto ? now : null;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        Guid? pushNotificationId = null;
        if (resident.DeliveryApprovalMode == DeliveryApprovalMode.Manual)
        {
            pushNotificationId = pushNotificationService.EnqueueDeliveryApprovalRequest(
                resident.UserId,
                deliveryRequest.Id,
                deliveryRequest.Locker.Code);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (pushNotificationId.HasValue)
        {
            await pushNotificationService.TrySendAsync(pushNotificationId.Value, cancellationToken);
        }

        return MapSummary(deliveryRequest);
    }

    public async Task<GuestDeliveryStatusResponse> GetAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindValidSessionAsync(id, guestSessionToken, cancellationToken);

        if (deliveryRequest.Status == DeliveryRequestStatus.PendingApproval &&
            deliveryRequest.ApprovalExpiresAt <= timeProvider.GetUtcNow())
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            deliveryRequest.UpdatedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        string? compartmentCode = deliveryRequest.AllocatedCompartmentId.HasValue
            ? await dbContext.LockerCompartments
                .Where(item => item.Id == deliveryRequest.AllocatedCompartmentId.Value)
                .Select(item => item.Code)
                .SingleAsync(cancellationToken)
            : null;
        Guid? parcelId = await dbContext.Parcels
            .Where(item => item.DeliveryRequestId == deliveryRequest.Id)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return new GuestDeliveryStatusResponse(
            deliveryRequest.Id,
            deliveryRequest.Status,
            deliveryRequest.ApprovalExpiresAt,
            compartmentCode,
            deliveryRequest.ReservationExpiresAt,
            parcelId,
            deliveryRequest.FailureCode,
            deliveryRequest.FailureDetail);
    }

    public async Task<int> ExpireStartedSessionsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<DeliveryRequest> expiredSessions = await dbContext.DeliveryRequests
            .Where(request =>
                request.Status == DeliveryRequestStatus.Started &&
                request.SessionExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (DeliveryRequest request in expiredSessions)
        {
            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.SessionExpired;
            request.UpdatedAt = now;
        }

        if (expiredSessions.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return expiredSessions.Count;
    }

    // ==========================================
    // Issue #20: Resident Delivery Approval
    // ==========================================

    public async Task<PagedResult<PendingDeliveryRequestResponse>> GetPendingRequestsForResidentAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        ResidentProfile resident = await GetActiveResidentProfileAsync(residentUserId, cancellationToken);

        IQueryable<DeliveryRequest> query = dbContext.DeliveryRequests
            .AsNoTracking()
            .Include(r => r.Locker)
            .Include(r => r.SystemPolicy)
            .Where(r => r.ResidentProfileId == resident.Id &&
                        r.Status == DeliveryRequestStatus.PendingApproval &&
                        (r.ApprovalExpiresAt == null || r.ApprovalExpiresAt > now));
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await query.CountAsync(cancellationToken);
        List<DeliveryRequest> pendingRequests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        List<PendingDeliveryRequestResponse> items = pendingRequests.Select(r => new PendingDeliveryRequestResponse(
            r.Id,
            r.Locker.Code,
            r.Locker.Address,
            r.ParcelImageUrl,
            r.RecipientPhoneSnapshot,
            r.CreatedAt,
            r.ApprovalExpiresAt ?? r.CreatedAt.AddMinutes(r.SystemPolicy.ManualApprovalTimeoutMinutes)
        )).ToList();
        return new PagedResult<PendingDeliveryRequestResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<DeliveryRequestSummaryResponse> ApproveDeliveryRequestAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        ResidentProfile resident = await GetActiveResidentProfileAsync(residentUserId, cancellationToken);

        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(r => r.SystemPolicy)
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu giao hàng.");

        if (deliveryRequest.ResidentProfileId != resident.Id)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền duyệt yêu cầu giao hàng này.");
        }

        if (deliveryRequest.Status != DeliveryRequestStatus.PendingApproval)
        {
            throw new InvalidOperationException($"Không thể duyệt yêu cầu ở trạng thái {deliveryRequest.Status}.");
        }

        if (deliveryRequest.ApprovalExpiresAt.HasValue && deliveryRequest.ApprovalExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new TimeoutException("Thời hạn phê duyệt yêu cầu giao hàng đã hết.");
        }

        deliveryRequest.Status = DeliveryRequestStatus.Approved;
        deliveryRequest.DecisionAt = now;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapSummary(deliveryRequest);
    }

    public async Task<DeliveryRequestSummaryResponse> RejectDeliveryRequestAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        ResidentProfile resident = await GetActiveResidentProfileAsync(residentUserId, cancellationToken);

        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(r => r.SystemPolicy)
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu giao hàng.");

        if (deliveryRequest.ResidentProfileId != resident.Id)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền từ chối yêu cầu giao hàng này.");
        }

        if (deliveryRequest.Status != DeliveryRequestStatus.PendingApproval)
        {
            throw new InvalidOperationException($"Không thể từ chối yêu cầu ở trạng thái {deliveryRequest.Status}.");
        }

        if (deliveryRequest.ApprovalExpiresAt.HasValue && deliveryRequest.ApprovalExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new TimeoutException("Thời hạn phê duyệt yêu cầu giao hàng đã hết.");
        }

        deliveryRequest.Status = DeliveryRequestStatus.Rejected;
        deliveryRequest.DecisionAt = now;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapSummary(deliveryRequest);
    }

    public async Task<int> ExpirePendingApprovalsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<DeliveryRequest> expiredRequests = await dbContext.DeliveryRequests
            .Where(r => r.Status == DeliveryRequestStatus.PendingApproval &&
                        r.ApprovalExpiresAt != null &&
                        r.ApprovalExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (DeliveryRequest request in expiredRequests)
        {
            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            request.UpdatedAt = now;
        }

        if (expiredRequests.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return expiredRequests.Count;
    }

    public async Task<CompartmentReservationResponse> OpenCompartmentAsync(
        Guid requestId,
        string guestSessionToken,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindValidSessionAsync(requestId, guestSessionToken, cancellationToken);
        if (deliveryRequest.Status != DeliveryRequestStatus.Approved)
        {
            throw new InvalidOperationException($"Chưa thể phân bổ ngăn tủ cho yêu cầu ở trạng thái {deliveryRequest.Status}.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        DateTimeOffset reservationExpiresAt = now.AddMinutes(deliveryRequest.SystemPolicy.CompartmentReservationMinutes);
        CompartmentReservation? reservation = await compartmentAllocationService.ReserveAvailableAsync(
            deliveryRequest.LockerId,
            deliveryRequest.Id,
            null,
            reservationExpiresAt,
            cancellationToken);
        if (reservation is null)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Failed;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.NoCompartment;
            deliveryRequest.FailureDetail = "Không có ngăn locker trống khả dụng.";
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            throw new InvalidOperationException("Không tìm thấy ngăn tủ trống khả dụng tại locker này.");
        }

        LockerCompartment availableCompartment = reservation.LockerCompartment;
        deliveryRequest.AllocatedCompartmentId = reservation.LockerCompartmentId;
        deliveryRequest.Status = DeliveryRequestStatus.Allocated;
        deliveryRequest.AllocatedAt = now;
        deliveryRequest.ReservationExpiresAt = reservationExpiresAt;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        OpenLockerResponse access = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            deliveryRequest.LockerId,
            availableCompartment.Id,
            null,
            deliveryRequest.Id,
            null,
            null,
            LockerAccessType.ShipperDropOff,
            LockerAccessMethod.GuestSession,
            null,
            "Guest Web"), cancellationToken);
        if (access.Result != LockerAccessResult.Succeeded)
        {
            await compartmentAllocationService.ReleaseAsync(deliveryRequest.Id, null, now, cancellationToken);
            deliveryRequest.Status = DeliveryRequestStatus.Failed;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.DeviceUnavailable;
            deliveryRequest.FailureDetail = access.FailureReason;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(access.FailureReason ?? "Không thể mở ngăn locker.");
        }

        return new CompartmentReservationResponse(
            deliveryRequest.Id,
            reservation.Id,
            availableCompartment.Id,
            availableCompartment.Code,
            reservation.ReservedAt,
            reservation.ExpiresAt);
    }

    public async Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<DeliveryRequest> expiredReservations = await dbContext.DeliveryRequests
            .Where(r => r.Status == DeliveryRequestStatus.Allocated &&
                        r.ReservationExpiresAt != null &&
                        r.ReservationExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (DeliveryRequest request in expiredReservations)
        {
            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.ReservationExpired;
            request.UpdatedAt = now;

            await compartmentAllocationService.ReleaseAsync(request.Id, null, now, cancellationToken);
        }

        if (expiredReservations.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return expiredReservations.Count;
    }

    public async Task FinalizeDropOffAsync(
        Guid requestId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(item => item.SystemPolicy)
            .Include(item => item.Locker)
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu giao hàng.");
        if (deliveryRequest.Status == DeliveryRequestStatus.Deposited)
        {
            return;
        }
        if (deliveryRequest.Status != DeliveryRequestStatus.Allocated)
        {
            throw new InvalidOperationException($"Không thể xác nhận gửi hàng khi yêu cầu ở trạng thái {deliveryRequest.Status}.");
        }

        if (!deliveryRequest.AllocatedCompartmentId.HasValue)
        {
            throw new InvalidOperationException("Yêu cầu giao hàng chưa được phân bổ ngăn tủ.");
        }

        DateTimeOffset now = completedAt;

        if (deliveryRequest.ReservationExpiresAt.HasValue && deliveryRequest.ReservationExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ReservationExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new TimeoutException("Thời gian giữ ngăn tủ đã hết hạn.");
        }

        Parcel parcel = new()
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryRequest.Id,
            ParcelCode = $"P-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Status = ParcelStatus.Stored,
            StoredAt = now,
            PickupDueAt = now.AddHours(deliveryRequest.SystemPolicy.OverdueStartAfterHours),
            MaxStorageUntil = now.AddHours(deliveryRequest.SystemPolicy.MaxStorageHours),
            CreatedAt = now,
            UpdatedAt = now
        };

        ParcelStatusHistory history = new()
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = null,
            ToStatus = ParcelStatus.Stored,
            Reason = "Bưu kiện đã được Shipper đặt vào ngăn tủ thành công.",
            ChangedAt = now
        };

        await compartmentAllocationService.ReleaseAsync(deliveryRequest.Id, null, now, cancellationToken);

        deliveryRequest.Status = DeliveryRequestStatus.Deposited;
        deliveryRequest.DepositedAt = now;
        deliveryRequest.CompartmentReleasedAt = now;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        dbContext.Parcels.Add(parcel);
        dbContext.ParcelStatusHistories.Add(history);
        Guid residentUserId = await dbContext.ResidentProfiles
            .Where(item => item.Id == deliveryRequest.ResidentProfileId)
            .Select(item => item.UserId)
            .SingleAsync(cancellationToken);
        string compartmentCode = await dbContext.LockerCompartments
            .Where(item => item.Id == deliveryRequest.AllocatedCompartmentId.Value)
            .Select(item => item.Code)
            .SingleAsync(cancellationToken);
        Guid pushNotificationId = pushNotificationService.EnqueueParcelStored(
            residentUserId,
            deliveryRequest.Id,
            parcel.Id,
            deliveryRequest.Locker.Code,
            compartmentCode);
        await dbContext.SaveChangesAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushNotificationId, cancellationToken);

    }

    // ==========================================
    // Private Helpers
    // ==========================================

    private async Task<DeliveryRequest> FindStartedSessionAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken)
    {
        DeliveryRequest deliveryRequest = await FindValidSessionAsync(id, guestSessionToken, cancellationToken);

        if (deliveryRequest.Status != DeliveryRequestStatus.Started)
        {
            throw new InvalidOperationException($"Không thể cập nhật yêu cầu ở trạng thái {deliveryRequest.Status}.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (deliveryRequest.SessionExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.SessionExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new TimeoutException("Phiên gửi hàng đã hết hạn.");
        }

        return deliveryRequest;
    }

    private async Task<DeliveryRequest> FindValidSessionAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(guestSessionToken))
        {
            throw new System.Security.Authentication.AuthenticationException("Thiếu X-Guest-Session-Token.");
        }

        string tokenHash = tokenHashService.HashToken(guestSessionToken.Trim());
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(request => request.SystemPolicy)
            .Include(request => request.Locker)
            .SingleOrDefaultAsync(request => request.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu giao hàng.");

        if (!string.Equals(deliveryRequest.GuestSessionTokenHash, tokenHash, StringComparison.Ordinal))
        {
            throw new System.Security.Authentication.AuthenticationException("Guest session token không hợp lệ.");
        }

        return deliveryRequest;
    }

    private async Task<ResidentProfile> GetActiveResidentProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.ResidentProfiles
            .Include(p => p.User)
            .SingleOrDefaultAsync(p => p.UserId == userId && p.User.Status == UserStatus.Active, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ Cư dân hợp lệ.");

    private static void RefreshSession(DeliveryRequest request, DateTimeOffset now)
    {
        request.LastActivityAt = now;
        request.SessionExpiresAt = now.AddMinutes(request.SystemPolicy.GuestSessionTimeoutMinutes);
        request.UpdatedAt = now;
    }

    private static string ValidateImageUrl(string value)
    {
        string url = RequireValue(value, nameof(value), 2048);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("ParcelImageUrl phải là URL HTTP hoặc HTTPS tuyệt đối.", nameof(value));
        }

        return url;
    }

    private static string RequireValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Giá trị không được để trống.", parameterName);
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Giá trị không được vượt quá {maxLength} ký tự.", parameterName);
        }

        return trimmed;
    }

    private static DeliveryRequestSummaryResponse MapSummary(DeliveryRequest request) =>
        new(request.Id, request.Status, request.ParcelImageUrl, request.SessionExpiresAt);
}
