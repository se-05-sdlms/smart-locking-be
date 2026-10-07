using Microsoft.EntityFrameworkCore;
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
    TimeProvider timeProvider,
    ILockerAccessService lockerAccessService) : IDeliveryRequestService
{
    // ==========================================
    // Issue #19: Guest Shipper Initiate & Submit
    // ==========================================

    public async Task<InitiateDeliveryResponse> InitiateAsync(
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

    public async Task<DeliveryRequestSummaryResponse> UploadImageAsync(
        Guid id,
        string guestSessionToken,
        UploadParcelImageRequest request,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);
        string parcelImageUrl = ValidateImageUrl(request.ParcelImageUrl);
        DateTimeOffset now = timeProvider.GetUtcNow();

        deliveryRequest.ParcelImageUrl = parcelImageUrl;
        RefreshSession(deliveryRequest, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapSummary(deliveryRequest);
    }

    public async Task<DeliveryRequestSummaryResponse> SubmitRecipientAsync(
        Guid id,
        string guestSessionToken,
        SubmitRecipientPhoneRequest request,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);
        string recipientPhone = RequireValue(request.RecipientPhone, nameof(request.RecipientPhone), 20);
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (deliveryRequest.ParcelImageUrl is null)
        {
            throw new InvalidOperationException("Phải cung cấp URL ảnh bưu kiện trước khi gửi thông tin người nhận.");
        }

        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(profile => profile.User)
            .SingleOrDefaultAsync(
                profile => profile.User.PhoneNumber == recipientPhone &&
                           profile.User.Role == UserRole.Resident &&
                           profile.User.Status == UserStatus.Active,
                cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy Cư dân đang hoạt động với số điện thoại đã nhập.");

        deliveryRequest.ResidentProfileId = resident.Id;
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

    public async Task<IReadOnlyCollection<PendingDeliveryRequestResponse>> GetPendingRequestsForResidentAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        ResidentProfile resident = await GetActiveResidentProfileAsync(residentUserId, cancellationToken);

        List<DeliveryRequest> pendingRequests = await dbContext.DeliveryRequests
            .AsNoTracking()
            .Include(r => r.Locker)
            .Include(r => r.SystemPolicy)
            .Where(r => r.ResidentProfileId == resident.Id &&
                        r.Status == DeliveryRequestStatus.PendingApproval &&
                        (r.ApprovalExpiresAt == null || r.ApprovalExpiresAt > now))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return pendingRequests.Select(r => new PendingDeliveryRequestResponse(
            r.Id,
            r.Locker.Code,
            r.Locker.Address,
            r.ParcelImageUrl,
            r.RecipientPhoneSnapshot,
            r.CreatedAt,
            r.ApprovalExpiresAt ?? r.CreatedAt.AddMinutes(r.SystemPolicy.ManualApprovalTimeoutMinutes)
        )).ToList();
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

    // ==========================================
    // Shipper: reserve and open a compartment
    // ==========================================

    public async Task<OpenCompartmentResponse> OpenCompartmentAsync(
        Guid requestId,
        string guestSessionToken,
        string? ipAddress,
        string? deviceContext,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await FindValidSessionAsync(requestId, guestSessionToken, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        LockerCompartment compartment;
        if (deliveryRequest.Status == DeliveryRequestStatus.Approved)
        {
            LockerCompartment? available = await dbContext.LockerCompartments
                .Where(c => c.LockerId == deliveryRequest.LockerId &&
                            c.OperationalStatus == LockerCompartmentOperationalStatus.Operational &&
                            !dbContext.Parcels.Any(p => p.DeliveryRequest.AllocatedCompartmentId == c.Id &&
                                (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue)) &&
                            !dbContext.CompartmentReservations.Any(r =>
                                r.LockerCompartmentId == c.Id && r.ReleasedAt == null))
                .OrderBy(c => c.Code)
                .FirstOrDefaultAsync(cancellationToken);

            if (available is null)
            {
                deliveryRequest.Status = DeliveryRequestStatus.Failed;
                deliveryRequest.FailureCode = DeliveryRequestFailureCode.NoCompartment;
                deliveryRequest.FailureDetail = "Không có ngăn locker trống khả dụng.";
                deliveryRequest.UpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("Không tìm thấy ngăn tủ trống khả dụng tại locker này.");
            }

            compartment = available;
            deliveryRequest.AllocatedCompartmentId = compartment.Id;
            deliveryRequest.Status = DeliveryRequestStatus.Allocated;
            deliveryRequest.AllocatedAt = now;
            deliveryRequest.ReservationExpiresAt = now.AddMinutes(deliveryRequest.SystemPolicy.CompartmentReservationMinutes);
            deliveryRequest.LastActivityAt = now;
            deliveryRequest.UpdatedAt = now;
            dbContext.CompartmentReservations.Add(new CompartmentReservation
            {
                Id = Guid.NewGuid(),
                LockerCompartmentId = compartment.Id,
                DeliveryRequestId = deliveryRequest.Id,
                ReservedAt = now,
                ExpiresAt = deliveryRequest.ReservationExpiresAt.Value,
                CreatedAt = now,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (deliveryRequest.Status == DeliveryRequestStatus.Allocated &&
                 deliveryRequest.ReservationExpiresAt > now &&
                 deliveryRequest.AllocatedCompartmentId.HasValue)
        {
            compartment = await dbContext.LockerCompartments.SingleAsync(
                c => c.Id == deliveryRequest.AllocatedCompartmentId.Value &&
                     dbContext.CompartmentReservations.Any(r => r.DeliveryRequestId == requestId &&
                         r.LockerCompartmentId == c.Id && r.ReleasedAt == null && r.ExpiresAt > now),
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Không thể mở ngăn tủ cho yêu cầu ở trạng thái {deliveryRequest.Status} hoặc đã hết hạn giữ ngăn.");
        }

        OpenLockerResponse access = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            deliveryRequest.LockerId,
            compartment.Id,
            null,
            deliveryRequest.Id,
            null,
            null,
            LockerAccessType.ShipperDropOff,
            LockerAccessMethod.GuestSession,
            ipAddress,
            deviceContext), cancellationToken);

        return new OpenCompartmentResponse(
            deliveryRequest.Id,
            compartment.Id,
            compartment.Code,
            access.AccessEventId,
            access.Result,
            access.FailureReason,
            deliveryRequest.ReservationExpiresAt!.Value);
    }

    public async Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Guid> candidateIds = await dbContext.DeliveryRequests
            .Where(r => r.Status == DeliveryRequestStatus.Allocated &&
                        r.ReservationExpiresAt != null && r.ReservationExpiresAt <= now &&
                        !dbContext.LockerAccessEvents.Any(e => e.DeliveryRequestId == r.Id &&
                            e.Result == LockerAccessResult.Succeeded && e.CompletedAt == null))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        int expiredCount = 0;
        foreach (Guid requestId in candidateIds)
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            if (transaction is not null)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"DeliveryRequest\" WHERE \"Id\" = {requestId} FOR UPDATE", cancellationToken);
            }

            // Recheck under the same row lock used when a shipper's pending access is created.
            DeliveryRequest request = await dbContext.DeliveryRequests.SingleAsync(r => r.Id == requestId, cancellationToken);
            await dbContext.Entry(request).ReloadAsync(cancellationToken);
            if (request.Status != DeliveryRequestStatus.Allocated || request.ReservationExpiresAt is null ||
                request.ReservationExpiresAt > now ||
                await dbContext.LockerAccessEvents.AnyAsync(e => e.DeliveryRequestId == requestId &&
                    e.Result == LockerAccessResult.Succeeded && e.CompletedAt == null, cancellationToken))
            {
                continue;
            }

            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.ReservationExpired;
            request.UpdatedAt = now;
            List<CompartmentReservation> reservations = await dbContext.CompartmentReservations
                .Where(r => r.DeliveryRequestId == requestId && r.ReleasedAt == null)
                .ToListAsync(cancellationToken);
            foreach (CompartmentReservation reservation in reservations)
            {
                reservation.ReleasedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            expiredCount++;
        }
        return expiredCount;
    }
    // ==========================================
    // Sensor-confirmed shipper drop-off
    // ==========================================

    public async Task FinalizeDropOffAsync(
        Guid requestId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(r => r.SystemPolicy)
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu giao hàng.");
        if (deliveryRequest.Status == DeliveryRequestStatus.Deposited)
        {
            return;
        }
        if (deliveryRequest.Status != DeliveryRequestStatus.Allocated ||
            !deliveryRequest.AllocatedCompartmentId.HasValue)
        {
            throw new InvalidOperationException("Yêu cầu giao hàng chưa được phân bổ ngăn tủ.");
        }

        Parcel parcel = new()
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryRequest.Id,
            ParcelCode = $"P-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Status = ParcelStatus.Stored,
            StoredAt = at,
            PickupDueAt = at.AddHours(deliveryRequest.SystemPolicy.OverdueStartAfterHours),
            MaxStorageUntil = at.AddHours(deliveryRequest.SystemPolicy.MaxStorageHours),
            CreatedAt = at,
            UpdatedAt = at
        };

        ParcelStatusHistory history = new()
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = null,
            ToStatus = ParcelStatus.Stored,
            Reason = "Bưu kiện đã được Shipper đặt vào ngăn tủ thành công.",
            ChangedAt = at
        };

        List<CompartmentReservation> reservations = await dbContext.CompartmentReservations
            .Where(r => r.DeliveryRequestId == deliveryRequest.Id && r.ReleasedAt == null)
            .ToListAsync(cancellationToken);

        foreach (CompartmentReservation reservation in reservations)
        {
            reservation.ReleasedAt = at;
        }

        deliveryRequest.Status = DeliveryRequestStatus.Deposited;
        deliveryRequest.DepositedAt = at;
        deliveryRequest.CompartmentReleasedAt = null;
        deliveryRequest.LastActivityAt = at;
        deliveryRequest.UpdatedAt = at;

        dbContext.Parcels.Add(parcel);
        dbContext.ParcelStatusHistories.Add(history);
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
            throw new UnauthorizedAccessException("Thiếu X-Guest-Session-Token.");
        }

        string tokenHash = tokenHashService.HashToken(guestSessionToken.Trim());
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(request => request.SystemPolicy)
            .Include(request => request.Locker)
            .SingleOrDefaultAsync(request => request.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu giao hàng.");

        if (!string.Equals(deliveryRequest.GuestSessionTokenHash, tokenHash, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Guest session token không hợp lệ.");
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
