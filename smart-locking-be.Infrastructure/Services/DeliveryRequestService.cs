using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.DTOs.Subscriptions;
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
    IImageStorageService imageStorageService,
    IWaybillOcrService waybillOcrService,
    IParcelQuotaService parcelQuotaService,
    TimeProvider timeProvider,
    IOperationsRealtimeNotifier? realtimeNotifier = null) : IDeliveryRequestService
{
    private const string WaybillScannedAction = "DeliveryRequest.WaybillScanned";
    private const string RecipientLookupAction = "DeliveryRequest.RecipientLookedUp";
    private const int MaxWaybillScansPerSession = 3;
    private const int MaxRecipientLookupsPerSession = 5;

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

        if (!await dbContext.OperationalLockers().AnyAsync(item => item.Id == locker.Id, cancellationToken))
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
        await using IDbContextTransaction? transaction = await LockDecisionAsync(id, cancellationToken);
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);
        string recipientPhone = RequireValue(request.RecipientPhone, nameof(request.RecipientPhone), 20);
        string parcelImageUrl = string.IsNullOrWhiteSpace(request.ParcelImageUrl)
            ? deliveryRequest.ParcelImageUrl
                ?? throw BadRequestException.For("parcelImageUrl", "Parcel image is required. Upload it with :waybill-ocr first.")
            : ValidateImageUrl(request.ParcelImageUrl);
        DateTimeOffset now = timeProvider.GetUtcNow();

        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(profile => profile.User)
            .SingleOrDefaultAsync(
                profile => profile.User.PhoneNumber == recipientPhone &&
                           profile.User.Role == UserRole.Resident &&
                           profile.User.Status == UserStatus.Active,
                cancellationToken)
            ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "No active resident was found for this phone number.");

        // Count and claim one unit of the purchased quota in the same resident lock.
        // The decision transaction also owns the resident quota lock.
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"User\" WHERE \"Id\" = {resident.UserId} FOR UPDATE", cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"ResidentProfile\" WHERE \"Id\" = {resident.Id} FOR UPDATE", cancellationToken);
            await dbContext.Entry(resident).ReloadAsync(cancellationToken);
            await dbContext.Entry(resident.User).ReloadAsync(cancellationToken);
            if (resident.RegisteredLockerId != deliveryRequest.LockerId || resident.User.PhoneNumber != recipientPhone || resident.User.Status != UserStatus.Active)
                throw new ConflictException(DeliveryErrorCodes.InvalidState, "Resident details changed. Look up the recipient again.");
            await dbContext.Entry(deliveryRequest).ReloadAsync(cancellationToken);
            if (deliveryRequest.Status != DeliveryRequestStatus.Started)
                throw new ConflictException(DeliveryErrorCodes.InvalidState, "Delivery request has already been submitted.");
            if (deliveryRequest.SessionExpiresAt <= timeProvider.GetUtcNow())
                throw new GoneException(DeliveryErrorCodes.SessionExpired, "Delivery session has expired.");
        }
        // A phone/locker approval may have won while this guest request was waiting for the lock.
        if (resident.User.PhoneNumber != recipientPhone || resident.User.Status != UserStatus.Active || resident.User.Role != UserRole.Resident)
            throw new NotFoundException(DeliveryErrorCodes.NotFound, "No active resident was found for this phone number.");
        if (resident.RegisteredLockerId != deliveryRequest.LockerId)
            throw new ConflictException(DeliveryErrorCodes.InvalidState, "Resident is not registered to receive parcels at this locker.");
        await EnsureResidentCanReceiveAsync(resident.Id, cancellationToken);

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
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        if (pushNotificationId.HasValue)
        {
            await pushNotificationService.TrySendAsync(pushNotificationId.Value, cancellationToken);
        }

        return MapSummary(deliveryRequest);
    }

    public async Task<WaybillOcrResponse> ScanWaybillAsync(
        Guid id,
        string guestSessionToken,
        Stream image,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction? transaction = await LockDecisionAsync(id, cancellationToken);
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);

        int attempts = await CountAuditActionsAsync(deliveryRequest.Id, WaybillScannedAction, cancellationToken);
        if (attempts >= MaxWaybillScansPerSession)
        {
            throw new TooManyRequestsException(
                DeliveryErrorCodes.OcrLimitReached,
                "Waybill scan limit reached for this session. Enter the phone number manually.");
        }

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await image.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        // Upload (also validates the image) and OCR run together so the Shipper never waits on both in turn.
        Task<string> uploadTask = imageStorageService.UploadAsync(
            new MemoryStream(bytes), fileName, contentType, cancellationToken);
        Task<WaybillOcrResult> ocrTask = waybillOcrService.ExtractRecipientPhonesAsync(
            bytes, contentType, cancellationToken);
        await Task.WhenAll(uploadTask, ocrTask);

        string parcelImageUrl = await uploadTask;
        WaybillOcrResult ocr = await ocrTask;
        DateTimeOffset now = timeProvider.GetUtcNow();

        deliveryRequest.ParcelImageUrl = parcelImageUrl;
        deliveryRequest.OcrExtractedPhone = ocr.Succeeded ? ocr.Phones[0] : null;
        deliveryRequest.OcrStatus = ocr.Succeeded ? OcrStatus.Succeeded : OcrStatus.Failed;
        RefreshSession(deliveryRequest, now);
        AddAudit(null, WaybillScannedAction, deliveryRequest, deliveryRequest.OcrStatus.ToString(), now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return new WaybillOcrResponse(
            ocr.Succeeded ? "Succeeded" : "Failed",
            ocr.Phones,
            ocr.IsMasked,
            parcelImageUrl);
    }

    public async Task<ResolveRecipientResponse> ResolveRecipientAsync(
        Guid id,
        string guestSessionToken,
        ResolveRecipientRequest request,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction? transaction = await LockDecisionAsync(id, cancellationToken);
        DeliveryRequest deliveryRequest = await FindStartedSessionAsync(id, guestSessionToken, cancellationToken);
        string phone = VietnamPhoneNumber.Normalize(request.Phone)
            ?? throw BadRequestException.For("phone", "Enter a valid Vietnamese mobile phone number of 10 digits.");

        // Lookup is only available after the parcel photo exists, to make bulk phone probing pointless.
        if (string.IsNullOrWhiteSpace(deliveryRequest.ParcelImageUrl))
        {
            throw new ConflictException(
                DeliveryErrorCodes.ParcelImageRequired,
                "Photograph the parcel before looking up the recipient.");
        }

        int lookups = await CountAuditActionsAsync(deliveryRequest.Id, RecipientLookupAction, cancellationToken);
        if (lookups >= MaxRecipientLookupsPerSession)
        {
            throw new TooManyRequestsException(
                DeliveryErrorCodes.LookupLimitReached,
                "Recipient lookup limit reached for this session.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var recipient = await dbContext.ResidentProfiles
            .Where(profile => profile.User.PhoneNumber == phone &&
                              profile.User.Role == UserRole.Resident &&
                              profile.User.Status == UserStatus.Active &&
                              profile.RegisteredLockerId == deliveryRequest.LockerId)
            .Select(profile => new { profile.Id, profile.FullName })
            .FirstOrDefaultAsync(cancellationToken);
        string? fullName = recipient?.FullName;

        // The phone number itself is never written to the audit trail, only whether the lookup matched.
        AddAudit(null, RecipientLookupAction, deliveryRequest, fullName is null ? "NotFound" : "Found", now);
        RefreshSession(deliveryRequest, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        if (recipient is null)
        {
            throw new NotFoundException(
                DeliveryErrorCodes.RecipientNotFound,
                "This phone number is not registered to receive parcels at this locker.");
        }

        // Tell the Shipper now, before the photo and request are sent, that this resident cannot take another parcel.
        await EnsureResidentCanReceiveAsync(recipient.Id, cancellationToken);
        return new ResolveRecipientResponse(recipient.FullName);
    }

    // Residents need an active subscription with room left under MaxActiveParcels (issue #157).
    // The message stays generic on purpose: Shippers must not learn a resident's plan details.
    private async Task EnsureResidentCanReceiveAsync(Guid residentProfileId, CancellationToken cancellationToken)
    {
        ParcelQuotaResult quota = await parcelQuotaService.CheckAsync(residentProfileId, cancellationToken);
        if (quota.Decision != ParcelQuotaDecision.Allowed)
        {
            throw new ConflictException(
                DeliveryErrorCodes.RecipientCannotReceive,
                "This resident cannot receive more parcels right now.");
        }
    }

    private Task<int> CountAuditActionsAsync(Guid deliveryRequestId, string action, CancellationToken cancellationToken) =>
        dbContext.AuditLogs.CountAsync(
            log => log.EntityType == nameof(DeliveryRequest) &&
                   log.EntityId == deliveryRequestId &&
                   log.Action == action,
            cancellationToken);

    public async Task<GuestDeliveryStatusResponse> GetAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction? transaction = await LockDecisionAsync(id, cancellationToken);
        DeliveryRequest deliveryRequest = await FindValidSessionAsync(id, guestSessionToken, cancellationToken);

        if (deliveryRequest.Status == DeliveryRequestStatus.PendingApproval &&
            deliveryRequest.ApprovalExpiresAt <= timeProvider.GetUtcNow())
        {
            deliveryRequest.Status = DeliveryRequestStatus.Expired;
            deliveryRequest.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            deliveryRequest.UpdatedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

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

    public async Task<DeliveryRequestSummaryResponse> CancelAsync(
        Guid id,
        string guestSessionToken,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction? transaction = await LockDecisionAsync(id, cancellationToken);
        DeliveryRequest request = await FindValidSessionAsync(id, guestSessionToken, cancellationToken);
        if (request.Status == DeliveryRequestStatus.Cancelled) return MapSummary(request);
        if (request.Status != DeliveryRequestStatus.PendingApproval)
        {
            throw new ConflictException(DeliveryErrorCodes.InvalidState,
                "Only a delivery waiting for resident approval can be cancelled. An approved delivery requires operator assistance.");
        }
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (request.ApprovalExpiresAt <= now)
        {
            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            request.UpdatedAt = now;
            AddAudit(null, "DeliveryRequest.Expired", request, "Approval expired.", now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new GoneException(DeliveryErrorCodes.ApprovalExpired, "Delivery request approval period has expired.");
        }

        // A pending request should never have opened a door or stored a parcel. Fail closed
        // on inconsistent legacy data rather than releasing a compartment with physical work.
        if (request.DecisionAt.HasValue || request.AllocatedCompartmentId.HasValue ||
            await dbContext.Parcels.AnyAsync(p => p.DeliveryRequestId == id, cancellationToken) ||
            await dbContext.LockerAccessEvents.AnyAsync(e => e.DeliveryRequestId == id, cancellationToken) ||
            await dbContext.CompartmentReservations.AnyAsync(r => r.DeliveryRequestId == id && r.ReleasedAt == null, cancellationToken))
        {
            throw new ConflictException(DeliveryErrorCodes.InvalidState,
                "This delivery has physical activity or a reservation and requires operator assistance.");
        }

        request.Status = DeliveryRequestStatus.Cancelled;
        request.DecisionAt = now;
        request.LastActivityAt = now;
        request.UpdatedAt = now;
        List<Notification> approvalNotifications = await dbContext.Notifications
            .Where(n => n.DeliveryRequestId == id && n.Type == "DeliveryApprovalRequested")
            .ToListAsync(cancellationToken);
        foreach (Notification notification in approvalNotifications)
        {
            notification.Type = "DeliveryRequestCancelled";
            notification.Title = "Delivery request cancelled";
            notification.Message = "The shipper cancelled this delivery before approval. No action is required.";
            // Any retry now carries the cancelled state rather than an approval prompt.
        }
        Guid residentUserId = await dbContext.ResidentProfiles
            .Where(r => r.Id == request.ResidentProfileId).Select(r => r.UserId).SingleAsync(cancellationToken);
        Guid pushId = Guid.NewGuid();
        foreach (NotificationChannel channel in new[] { NotificationChannel.InApp, NotificationChannel.Push })
        {
            dbContext.Notifications.Add(new Notification
            {
                Id = channel == NotificationChannel.Push ? pushId : Guid.NewGuid(),
                UserId = residentUserId,
                DeliveryRequestId = id,
                Type = "DeliveryRequestCancelled",
                Channel = channel,
                Title = "Delivery request cancelled",
                Message = $"The shipper cancelled the delivery at locker {request.Locker.Code} before approval.",
                DeliveryStatus = channel == NotificationChannel.Push ? NotificationDeliveryStatus.Pending : NotificationDeliveryStatus.Sent,
                SentAt = channel == NotificationChannel.InApp ? now : null,
                CreatedAt = now
            });
        }
        AddAudit(null, "DeliveryRequest.Cancelled", request, "Owning guest session cancelled before approval.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        return MapSummary(request);
    }

    public async Task<ResidentDeliveryRequestResponse> GetForResidentAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        ResidentProfile resident = await GetActiveResidentProfileAsync(residentUserId, cancellationToken);
        DeliveryRequest request = await dbContext.DeliveryRequests.AsNoTracking().Include(r => r.Locker)
            .SingleOrDefaultAsync(r => r.Id == requestId && r.ResidentProfileId == resident.Id, cancellationToken)
            ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "Delivery request not found.");
        DeliveryRequestStatus status = request.Status == DeliveryRequestStatus.PendingApproval &&
            request.ApprovalExpiresAt <= timeProvider.GetUtcNow() ? DeliveryRequestStatus.Expired : request.Status;
        return new ResidentDeliveryRequestResponse(request.Id, status, request.Locker.Code,
            request.Locker.Address, request.ParcelImageUrl, request.RecipientPhoneSnapshot,
            request.CreatedAt, request.ApprovalExpiresAt, request.DecisionAt);
    }

    public async Task<int> ExpireStartedSessionsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Guid> requestIds = await dbContext.DeliveryRequests.AsNoTracking()
            .Where(r => r.Status == DeliveryRequestStatus.Started && r.SessionExpiresAt <= now)
            .Select(r => r.Id).ToListAsync(cancellationToken);
        int expired = 0;
        foreach (Guid id in requestIds)
        {
            await using IDbContextTransaction? transaction = await LockDecisionAsync(id, cancellationToken);
            DeliveryRequest request = await dbContext.DeliveryRequests.SingleAsync(r => r.Id == id, cancellationToken);
            if (request.Status != DeliveryRequestStatus.Started || request.SessionExpiresAt > now) continue;
            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.SessionExpired;
            request.UpdatedAt = now;
            AddAudit(null, "DeliveryRequest.Expired", request, "Guest session expired.", now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            expired++;
        }
        return expired;
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
        await using IDbContextTransaction? transaction = await LockDecisionAsync(requestId, cancellationToken);
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
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new GoneException(DeliveryErrorCodes.ApprovalExpired, "Delivery request approval period has expired.");
        }

        deliveryRequest.Status = DeliveryRequestStatus.Approved;
        deliveryRequest.DecisionAt = now;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        AddAudit(residentUserId, "DeliveryRequest.Approved", deliveryRequest, null, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return MapSummary(deliveryRequest);
    }

    public async Task<DeliveryRequestSummaryResponse> RejectDeliveryRequestAsync(
        Guid residentUserId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction? transaction = await LockDecisionAsync(requestId, cancellationToken);
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
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new GoneException(DeliveryErrorCodes.ApprovalExpired, "Delivery request approval period has expired.");
        }

        deliveryRequest.Status = DeliveryRequestStatus.Rejected;
        deliveryRequest.DecisionAt = now;
        deliveryRequest.LastActivityAt = now;
        deliveryRequest.UpdatedAt = now;

        AddAudit(residentUserId, "DeliveryRequest.Rejected", deliveryRequest, null, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return MapSummary(deliveryRequest);
    }

    public async Task<int> ExpirePendingApprovalsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Guid> requestIds = await dbContext.DeliveryRequests.AsNoTracking()
            .Where(r => r.Status == DeliveryRequestStatus.PendingApproval && r.ApprovalExpiresAt <= now)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        int expired = 0;
        foreach (Guid id in requestIds)
        {
            await using IDbContextTransaction? transaction = await LockDecisionAsync(id, cancellationToken);
            DeliveryRequest request = await dbContext.DeliveryRequests.SingleAsync(r => r.Id == id, cancellationToken);
            if (request.Status != DeliveryRequestStatus.PendingApproval || request.ApprovalExpiresAt > now) continue;
            request.Status = DeliveryRequestStatus.Expired;
            request.FailureCode = DeliveryRequestFailureCode.ApprovalExpired;
            request.UpdatedAt = now;
            AddAudit(null, "DeliveryRequest.Expired", request, "Approval expired.", now);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            expired++;
        }
        return expired;
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

    // PostgreSQL row locks serialize all approval decisions across API/worker instances.
    // The lock is acquired before loading the request so a waiting contender sees the winner.
    // Nonrelational providers are retained for the existing in-memory service checks.
    private async Task<IDbContextTransaction?> LockDecisionAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return null;
        IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"DeliveryRequest\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

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
