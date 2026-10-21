using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
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
    TimeProvider timeProvider,
    IOperationsRealtimeNotifier? realtimeNotifier = null) : IDeliveryRequestService
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
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, $"Locker '{lockerCode}' not found.");

        if (locker.OperationalStatus != LockerOperationalStatus.Operational)
        {
            throw new ConflictException(LockerErrorCodes.NotOperational, "Locker is not ready to receive parcels.");
        }

        SystemPolicy policy = await dbContext.SystemPolicies.SingleOrDefaultAsync(
            item => item.IsActive &&
                    item.EffectiveFrom <= now &&
                    (item.EffectiveTo == null || item.EffectiveTo > now),
            cancellationToken)
            ?? throw new InternalServerException("No active system policy is configured.");

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
        AddAudit(null, "DeliveryRequest.Created", deliveryRequest, deliveryRequest.Status.ToString(), now);
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
            ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "No active resident was found for this phone number.");

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

        AddAudit(null, "DeliveryRequest.Submitted", deliveryRequest, deliveryRequest.Status.ToString(), now);
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
            AddAudit(null, "DeliveryRequest.Expired", request, "Guest session expired.", now);
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
            ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "Delivery request not found.");

        if (deliveryRequest.ResidentProfileId != resident.Id)
        {
            throw new ForbiddenException(DeliveryErrorCodes.AccessDenied, "You cannot approve this delivery request.");
        }

        if (deliveryRequest.Status != DeliveryRequestStatus.PendingApproval)
        {
            throw new ConflictException(DeliveryErrorCodes.InvalidState, $"Delivery request cannot be approved in state {deliveryRequest.Status}.");
        }

        if (deliveryRequest.ApprovalExpiresAt.HasValue && deliveryRequest.ApprovalExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new GoneException(DeliveryErrorCodes.ApprovalExpired, "Delivery request approval period has expired.");
        }

        deliveryRequest.Status = DeliveryRequestStatus.Approved;
        deliveryRequest.DecisionAt = now;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        AddAudit(residentUserId, "DeliveryRequest.Approved", deliveryRequest, null, now);
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
            ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "Delivery request not found.");

        if (deliveryRequest.ResidentProfileId != resident.Id)
        {
            throw new ForbiddenException(DeliveryErrorCodes.AccessDenied, "You cannot reject this delivery request.");
        }

        if (deliveryRequest.Status != DeliveryRequestStatus.PendingApproval)
        {
            throw new ConflictException(DeliveryErrorCodes.InvalidState, $"Delivery request cannot be rejected in state {deliveryRequest.Status}.");
        }

        if (deliveryRequest.ApprovalExpiresAt.HasValue && deliveryRequest.ApprovalExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new GoneException(DeliveryErrorCodes.ApprovalExpired, "Delivery request approval period has expired.");
        }

        deliveryRequest.Status = DeliveryRequestStatus.Rejected;
        deliveryRequest.DecisionAt = now;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        AddAudit(residentUserId, "DeliveryRequest.Rejected", deliveryRequest, null, now);
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
            AddAudit(null, "DeliveryRequest.Expired", request, "Approval expired.", now);
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
        CancellationToken cancellationToken = default, LockerAccessMethod accessMethod = LockerAccessMethod.GuestSession)
    {
        DeliveryRequest deliveryRequest = await FindValidSessionAsync(requestId, guestSessionToken, cancellationToken);
        if (deliveryRequest.Status != DeliveryRequestStatus.Approved)
        {
            throw new ConflictException(DeliveryErrorCodes.InvalidState, $"Delivery request cannot be allocated in state {deliveryRequest.Status}.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        DateTimeOffset reservationExpiresAt = now.AddMinutes(deliveryRequest.SystemPolicy.CompartmentReservationMinutes);
        CompartmentReservation? reservation = await compartmentAllocationService.ReserveAvailableAsync(
            deliveryRequest.LockerId,
            deliveryRequest.Id,
            null,
            reservationExpiresAt,
            cancellationToken,
            verifyDoorOnDevice: accessMethod == LockerAccessMethod.Bluetooth);
        if (reservation is null)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Failed;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.NoCompartment;
            deliveryRequest.FailureDetail = "No locker compartment is available.";
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            throw new ConflictException(LockerErrorCodes.NoAvailableCompartment, "No available compartment was found at this locker.");
        }

        LockerCompartment availableCompartment = reservation.LockerCompartment;
        deliveryRequest.AllocatedCompartmentId = reservation.LockerCompartmentId;
        deliveryRequest.Status = DeliveryRequestStatus.Allocated;
        deliveryRequest.AllocatedAt = now;
        deliveryRequest.ReservationExpiresAt = reservationExpiresAt;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        AddAudit(null, "DeliveryRequest.CompartmentAllocated", deliveryRequest, availableCompartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        OpenLockerResponse access = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            deliveryRequest.LockerId,
            availableCompartment.Id,
            null,
            deliveryRequest.Id,
            null,
            null,
            LockerAccessType.ShipperDropOff,
            accessMethod,
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
            throw access.Result == LockerAccessResult.Failed
                ? new ServiceUnavailableException(LockerErrorCodes.OpenFailed, "The locker compartment could not be opened.")
                : new ConflictException(LockerErrorCodes.OpenFailed, "The locker compartment could not be opened.");
        }

        return new CompartmentReservationResponse(
            deliveryRequest.Id,
            reservation.Id,
            availableCompartment.Id,
            availableCompartment.Code,
            reservation.ReservedAt,
            reservation.ExpiresAt, access.Bluetooth);
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
            AddAudit(null, "DeliveryRequest.Expired", request, "Compartment reservation expired.", now);

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
            ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "Delivery request not found.");
        if (deliveryRequest.Status == DeliveryRequestStatus.Deposited)
        {
            return;
        }
        if (deliveryRequest.Status != DeliveryRequestStatus.Allocated)
        {
            throw new ConflictException(DeliveryErrorCodes.InvalidState, $"Delivery request cannot be deposited in state {deliveryRequest.Status}.");
        }

        if (!deliveryRequest.AllocatedCompartmentId.HasValue)
        {
            throw new ConflictException(DeliveryErrorCodes.InvalidState, "Delivery request has no allocated compartment.");
        }

        DateTimeOffset now = completedAt;

        if (deliveryRequest.ReservationExpiresAt.HasValue && deliveryRequest.ReservationExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ReservationExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new GoneException(DeliveryErrorCodes.SessionExpired, "Compartment reservation has expired.");
        }

        (_, DateTimeOffset pickupDueAt, DateTimeOffset transferEligibleAt) = ParcelTimeline.GetDeadlines(
            now,
            deliveryRequest.SystemPolicy.OverdueStartAfterHours,
            deliveryRequest.SystemPolicy.MaxStorageHours);
        Parcel parcel = new()
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryRequest.Id,
            ParcelCode = $"P-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Status = ParcelStatus.Stored,
            StoredAt = now,
            PickupDueAt = pickupDueAt,
            MaxStorageUntil = transferEligibleAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        ParcelStatusHistory history = new()
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = null,
            ToStatus = ParcelStatus.Stored,
            Reason = "Shipper deposited the parcel in the locker compartment.",
            ChangedAt = now
        };

        await compartmentAllocationService.ReleaseAsync(deliveryRequest.Id, null, now, cancellationToken);

        deliveryRequest.Status = DeliveryRequestStatus.Deposited;
        deliveryRequest.DepositedAt = now;
        // Chỉ giải phóng ngăn sau khi nhận hàng, không phải khi vừa gửi vào tủ.
        deliveryRequest.CompartmentReleasedAt = null;
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
        AddAudit(null, "DeliveryRequest.Deposited", deliveryRequest, $"ParcelId={parcel.Id}", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        // Khi đồng bộ đang có transaction, worker sẽ gửi sau khi lưu thành công.
        if (dbContext.Database.CurrentTransaction is null)
            await pushNotificationService.TrySendAsync(pushNotificationId, cancellationToken);
        if (realtimeNotifier is not null)
        {
            await realtimeNotifier.PublishToLockerAsync(deliveryRequest.LockerId, new RealtimeEvent(
                "ParcelStored",
                parcel.Id,
                deliveryRequest.LockerId,
                parcel.Status.ToString(),
                "Parcel stored in locker.",
                now), cancellationToken);
        }

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
            throw new ConflictException(DeliveryErrorCodes.InvalidState, $"Delivery request cannot be updated in state {deliveryRequest.Status}.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (deliveryRequest.SessionExpiresAt <= now)
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.SessionExpired;
            deliveryRequest.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new GoneException(DeliveryErrorCodes.SessionExpired, "Delivery session has expired.");
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
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, "X-Guest-Session-Token is required.");
        }

        string tokenHash = tokenHashService.HashToken(guestSessionToken.Trim());
        DeliveryRequest deliveryRequest = await dbContext.DeliveryRequests
            .Include(request => request.SystemPolicy)
            .Include(request => request.Locker)
            .SingleOrDefaultAsync(request => request.Id == id, cancellationToken)
            ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "Delivery request not found.");

        if (!string.Equals(deliveryRequest.GuestSessionTokenHash, tokenHash, StringComparison.Ordinal))
        {
            throw new UnauthorizedException(AuthErrorCodes.InvalidGuestSession, "Guest session token is invalid.");
        }

        return deliveryRequest;
    }

    private async Task<ResidentProfile> GetActiveResidentProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.ResidentProfiles
            .Include(p => p.User)
            .SingleOrDefaultAsync(p => p.UserId == userId && p.User.Status == UserStatus.Active, cancellationToken)
            ?? throw new NotFoundException(UserErrorCodes.NotFound, "Valid resident profile not found.");

    private static void RefreshSession(DeliveryRequest request, DateTimeOffset now)
    {
        request.LastActivityAt = now;
        request.SessionExpiresAt = now.AddMinutes(request.SystemPolicy.GuestSessionTimeoutMinutes);
        request.UpdatedAt = now;
    }

    private static string ValidateImageUrl(string value)
    {
        string url = RequireValue(value, "ParcelImageUrl", 2048);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw BadRequestException.For("parcelImageUrl", "ParcelImageUrl must be an absolute HTTP or HTTPS URL.");
        }

        return url;
    }

    private static string RequireValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw BadRequestException.For(parameterName, $"{parameterName} is required.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw BadRequestException.For(parameterName, $"{parameterName} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static DeliveryRequestSummaryResponse MapSummary(DeliveryRequest request) =>
        new(request.Id, request.Status, request.ParcelImageUrl, request.SessionExpiresAt);

    private void AddAudit(Guid? actorUserId, string action, DeliveryRequest request, string? details, DateTimeOffset occurredAt) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = nameof(DeliveryRequest),
            EntityId = request.Id,
            Result = AuditLogResult.Succeeded,
            Details = details,
            OccurredAt = occurredAt
        });
}
