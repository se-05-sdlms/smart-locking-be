using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using System.Security.Cryptography;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ReturnRequestService(
    ApplicationDbContext dbContext,
    ILockerAccessService lockerAccessService,
    ICompartmentAllocationService compartmentAllocationService,
    IPushNotificationService pushNotificationService,
    ITokenHashService tokenHashService,
    TimeProvider timeProvider,
    IOperationsRealtimeNotifier? realtimeNotifier = null) : IReturnRequestService, IReturnPickupSessionService
{
    public async Task<ReturnRequestResponse> CreateAsync(Guid residentUserId, CreateReturnRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl)) throw BadRequestException.For("imageUrl", "Return image URL is required.");
        await using var residentTransaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"User\" WHERE \"Id\" = {residentUserId} FOR UPDATE", cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"ResidentProfile\" WHERE \"UserId\" = {residentUserId} FOR UPDATE", cancellationToken);
        }
        ResidentProfile? trackedResident = dbContext.ResidentProfiles.Local.FirstOrDefault(item => item.UserId == residentUserId);
        if (trackedResident is not null) await dbContext.Entry(trackedResident).ReloadAsync(cancellationToken);
        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(item => item.User).Include(item => item.RegisteredLocker)
            .SingleOrDefaultAsync(item => item.UserId == residentUserId, cancellationToken)
            ?? throw new NotFoundException(UserErrorCodes.NotFound, "Không tìm thấy hồ sơ cư dân.");
        if (dbContext.Database.IsRelational()) await dbContext.Entry(resident.User).ReloadAsync(cancellationToken);
        if (resident.User.Status != UserStatus.Active) throw new ConflictException(ReturnErrorCodes.InvalidState, "Tài khoản cư dân không hoạt động.");
        // Client truyền tủ đang kết nối; giữ cách gọi cũ cho app chưa cập nhật.
        Locker? locker = string.IsNullOrWhiteSpace(request.DeviceIdentifier) ? resident.RegisteredLocker :
            await dbContext.Lockers.SingleOrDefaultAsync(x => x.DeviceIdentifier == request.DeviceIdentifier, cancellationToken);
        if (locker is null || !await dbContext.OperationalLockers().AnyAsync(item => item.Id == locker.Id, cancellationToken))
            throw new ConflictException(LockerErrorCodes.NotOperational, "Chọn hoặc kết nối một tủ đang hoạt động trước khi gửi đồ.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        ReturnRequest entity = new()
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = resident.Id,
            LockerId = locker.Id,
            ReturnCode = await GeneratePickupCodeAsync(cancellationToken),
            ReturnReason = request.Note?.Trim(),
            ReturnImageUrl = request.ImageUrl.Trim(),
            Status = ReturnRequestStatus.Created,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.ReturnRequests.Add(entity);
        AddAudit(residentUserId, "ReturnRequest.Created", nameof(ReturnRequest), entity.Id, "Resident created a return request.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        entity.Locker = locker;
        if (residentTransaction is not null) await residentTransaction.CommitAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<PagedResult<ReturnRequestResponse>> GetMineAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20)
    {
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<ReturnRequest> query = ResidentQuery(residentUserId);
        int totalCount = await query.CountAsync(cancellationToken);
        ReturnRequest[] entities = await query.OrderByDescending(item => item.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<ReturnRequestResponse>(entities.Select(Map).ToArray(), totalCount, pageNumber, pageSize);
    }

    public async Task<ReturnRequestResponse> GetAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default) =>
        Map(await ResidentQuery(residentUserId).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found."));

    public async Task<ReturnUnlockResponse> OpenCompartmentAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default, LockerAccessMethod accessMethod = LockerAccessMethod.RemoteApp)
    {
        await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, id, cancellationToken);
        ReturnRequest entity = await ResidentQuery(residentUserId).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Cancellation is not null || entity.Status != ReturnRequestStatus.Created) throw new ConflictException(ReturnErrorCodes.InvalidState, "Return request cannot be allocated in its current state.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        DateTimeOffset expiresAt = now.AddMinutes(policy.CompartmentReservationMinutes);
        CompartmentReservation? reservation = await compartmentAllocationService.ReserveAvailableAsync(
            entity.LockerId,
            null,
            entity.Id,
            expiresAt,
            cancellationToken,
            verifyDoorOnDevice: accessMethod == LockerAccessMethod.Bluetooth);
        if (reservation is null) throw new ConflictException(LockerErrorCodes.NoAvailableCompartment, "Tủ hiện không còn ngăn trống.");
        LockerCompartment compartment = reservation.LockerCompartment;
        entity.AllocatedCompartmentId = compartment.Id;
        entity.AllocatedAt = now;
        entity.ReservationExpiresAt = expiresAt;
        entity.Status = ReturnRequestStatus.Allocated;
        entity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        OpenLockerResponse opened = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            entity.LockerId, compartment.Id, residentUserId, null, null, entity.Id,
            LockerAccessType.ResidentReturnDropOff, accessMethod, null, "Resident Mobile"), cancellationToken);
        if (opened.Result != LockerAccessResult.Succeeded)
        {
            await compartmentAllocationService.ReleaseAsync(null, entity.Id, now, cancellationToken);
            entity.Status = ReturnRequestStatus.Failed;
            entity.FailureReason = "The locker compartment could not be opened.";
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transition is not null) await transition.CommitAsync(cancellationToken);
            throw opened.Result == LockerAccessResult.Failed
                ? new ServiceUnavailableException(LockerErrorCodes.OpenFailed, entity.FailureReason)
                : new ConflictException(LockerErrorCodes.OpenFailed, entity.FailureReason);
        }
        AddAudit(residentUserId, "ReturnRequest.CompartmentOpened", nameof(ReturnRequest), entity.Id, compartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transition is not null) await transition.CommitAsync(cancellationToken);
        await PublishAsync(entity, cancellationToken);
        return new ReturnUnlockResponse(entity.Id, compartment.Code, opened.AccessEventId, expiresAt, opened.Bluetooth);
    }

    public async Task FinalizeDepositAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, id, cancellationToken);
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Status == ReturnRequestStatus.Deposited) return;
        if (entity.Status != ReturnRequestStatus.Allocated || entity.AllocatedCompartment is null)
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Return request has no allocated compartment.");
        DateTimeOffset now = completedAt;
        entity.Status = ReturnRequestStatus.Deposited;
        entity.ResidentDepositedAt = now;
        entity.UpdatedAt = now;
        await compartmentAllocationService.ReleaseAsync(null, entity.Id, now, cancellationToken);
        Guid pushId = pushNotificationService.EnqueueReturnNotification(
            entity.ResidentProfile.UserId, entity.Id, "ReturnDeposited", "Return parcel stored",
            entity.Cancellation is null
                ? $"Return parcel is stored in compartment {entity.AllocatedCompartment.Code}. Shipper pickup code: {entity.ReturnCode}."
                : "Cancellation is pending verification. Do not share the previous pickup code or try to retrieve the parcel yet.");
        AddAudit(entity.ResidentProfile.UserId, "ReturnRequest.Deposited", nameof(ReturnRequest), entity.Id, entity.AllocatedCompartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transition is not null) await transition.CommitAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        await PublishAsync(entity, cancellationToken);
    }

    public async Task<ReturnPickupSessionResponse> CreateAsync(ValidateReturnPickupRequest request, CancellationToken cancellationToken = default)
    {
        string code = request.PickupCode.Trim();
        if (code.Length != PickupCodeLength || !code.All(char.IsAsciiDigit)) throw BadRequestException.For("pickupCode", "Pickup code must contain exactly six digits.");
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.ReturnCode == code && item.Locker.Code == request.LockerCode.Trim(), cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.InvalidPickupCode, "Pickup code is invalid for this locker.");
        await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, entity.Id, cancellationToken);
        await dbContext.Entry(entity).Reference(item => item.Cancellation).LoadAsync(cancellationToken);
        if (entity.Cancellation is not null || entity.Status != ReturnRequestStatus.Deposited || entity.AllocatedCompartment is null)
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Pickup code has been used or is not ready.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        string token = tokenHashService.CreateSecureToken();
        entity.ShipperSessionTokenHash = tokenHashService.HashToken(token);
        entity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transition is not null) await transition.CommitAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, token, entity.Locker.Code, entity.AllocatedCompartment.Code, entity.ReturnImageUrl ?? string.Empty, now.AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task<ReturnPickupSessionResponse> GetAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default)
    {
        await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, id, cancellationToken);
        ReturnRequest? completed = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        if (completed?.Status == ReturnRequestStatus.Completed && completed.Cancellation is null
            && !string.IsNullOrWhiteSpace(completed.ShipperSessionTokenHash)
            && string.Equals(completed.ShipperSessionTokenHash, tokenHashService.HashToken(guestSessionToken.Trim()), StringComparison.Ordinal)
            && completed.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes) > timeProvider.GetUtcNow())
            return new ReturnPickupSessionResponse(completed.Id, guestSessionToken, completed.Locker.Code,
                completed.AllocatedCompartment!.Code, string.Empty, completed.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes), Completed: true);
        ReturnRequest entity = await GetValidGuestReturnAsync(id, guestSessionToken, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transition is not null) await transition.CommitAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, guestSessionToken, entity.Locker.Code, entity.AllocatedCompartment!.Code, entity.ReturnImageUrl ?? string.Empty, entity.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task<ReturnPickupSessionResponse> OpenCompartmentAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default, LockerAccessMethod accessMethod = LockerAccessMethod.GuestSession)
    {
        await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, id, cancellationToken);
        ReturnRequest entity = await GetValidGuestReturnAsync(id, guestSessionToken, cancellationToken);
        OpenLockerResponse opened = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            entity.LockerId, entity.AllocatedCompartmentId!.Value, null, null, null, entity.Id,
            LockerAccessType.ShipperReturnPickup, accessMethod, null, "Shipper Guest Web"), cancellationToken);
        if (opened.Result != LockerAccessResult.Succeeded) throw new ConflictException(LockerErrorCodes.OpenFailed, opened.FailureReason ?? "Không thể mở ngăn tủ.");
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transition is not null) await transition.CommitAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, guestSessionToken, entity.Locker.Code, entity.AllocatedCompartment!.Code, entity.ReturnImageUrl ?? string.Empty, timeProvider.GetUtcNow().AddMinutes(policy.GuestSessionTimeoutMinutes), opened.Bluetooth);
    }

    public async Task FinalizePickupAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, id, cancellationToken);
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Status == ReturnRequestStatus.Completed) return;
        if (entity.Status != ReturnRequestStatus.Deposited || entity.AllocatedCompartment is null)
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Return request cannot complete pickup in its current state.");
        DateTimeOffset now = completedAt;
        entity.Status = ReturnRequestStatus.Completed;
        entity.ShipperPickedUpAt = now;
        if (entity.Cancellation is not null)
        {
            entity.Cancellation.Status = "AlreadyCollected";
            entity.Cancellation.CompletedAt = now;
            AddAudit(null, "ReturnCancellation.AlreadyCollected", nameof(ReturnRequest), entity.Id, "An already dispatched shipper pickup completed; cancellation did not release the compartment.", now);
        }
        entity.CompartmentReleasedAt = now;
        // Retain the hash briefly for authenticated completion reads only; unlock still requires Deposited.
        if (entity.Cancellation is not null) entity.ShipperSessionTokenHash = null;
        entity.UpdatedAt = now;
        Guid pushId = pushNotificationService.EnqueueReturnNotification(
            entity.ResidentProfile.UserId, entity.Id, "ReturnPickedUp", "Shipper collected return parcel",
            $"Shipper collected the return parcel from locker {entity.Locker.Code}, compartment {entity.AllocatedCompartment.Code}.");
        AddAudit(null, "ReturnRequest.PickedUp", nameof(ReturnRequest), entity.Id, entity.AllocatedCompartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transition is not null) await transition.CommitAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        await PublishAsync(entity, cancellationToken);
    }

    public async Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid[] ids = await dbContext.ReturnRequests
            .Where(item => item.Status == ReturnRequestStatus.Allocated && item.ReservationExpiresAt <= now)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        int count = 0;
        foreach (Guid id in ids)
        {
            await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, id, cancellationToken);
            ReturnRequest item = await FullQuery().SingleAsync(item => item.Id == id, cancellationToken);
            if (item.Status != ReturnRequestStatus.Allocated || item.ReservationExpiresAt > now || item.Cancellation is not null) continue;
            item.Status = ReturnRequestStatus.Expired; item.UpdatedAt = now; item.CompartmentReleasedAt = now;
            await compartmentAllocationService.ReleaseAsync(null, item.Id, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transition is not null) await transition.CommitAsync(cancellationToken);
            await PublishAsync(item, cancellationToken);
            count++;
        }
        return count;
    }

    public async Task<ReturnRequestResponse> CancelAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var transition = await ReturnTransitionLock.AcquireAsync(dbContext, id, cancellationToken);
        ReturnRequest entity = await ResidentQuery(residentUserId).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Cancellation is not null) return Map(entity);
        if (entity.ResidentProfile.User.Status != UserStatus.Active)
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Resident account is inactive.");
        if (entity.Status is not (ReturnRequestStatus.Created or ReturnRequestStatus.Allocated or ReturnRequestStatus.Deposited))
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Return request can no longer be cancelled.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        bool neverAllocated = entity.Status == ReturnRequestStatus.Created && entity.AllocatedCompartmentId is null
            && entity.AllocatedAt is null && entity.ResidentDepositedAt is null
            && !await dbContext.LockerAccessEvents.AnyAsync(item => item.ReturnRequestId == id, cancellationToken);
        entity.Cancellation = new ReturnCancellationRequest
        {
            ReturnRequestId = id,
            ResidentUserId = residentUserId,
            RequestedAt = now,
            Status = neverAllocated ? "Cancelled" : "PendingVerification",
            CompletedAt = neverAllocated ? now : null
        };
        entity.ShipperSessionTokenHash = null;
        entity.UpdatedAt = now;
        if (neverAllocated)
        {
            entity.Status = ReturnRequestStatus.Cancelled;
            entity.CompartmentReleasedAt = now;
            await compartmentAllocationService.ReleaseAsync(null, id, now, cancellationToken);
        }
        AddAudit(residentUserId, "ReturnCancellation.Requested", nameof(ReturnRequest), id,
            neverAllocated ? "Cancelled before any compartment allocation or unlock." : "Web pickup revoked. Offline credential revocation and physical custody remain unverified; compartment retained.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transition is not null) await transition.CommitAsync(cancellationToken);
        await PublishAsync(entity, cancellationToken);
        return Map(entity);
    }

    private Task PublishAsync(ReturnRequest request, CancellationToken cancellationToken) =>
        realtimeNotifier?.PublishToLockerAsync(request.LockerId, new RealtimeEvent(
            "ReturnRequestUpdated",
            request.Id,
            request.LockerId,
            request.Status.ToString(),
            null,
            request.UpdatedAt), cancellationToken) ?? Task.CompletedTask;

    private IQueryable<ReturnRequest> ResidentQuery(Guid userId) => FullQuery().Where(item => item.ResidentProfile.UserId == userId);
    private IQueryable<ReturnRequest> FullQuery() => dbContext.ReturnRequests
        .Include(item => item.ResidentProfile).ThenInclude(profile => profile.User)
        .Include(item => item.Locker).Include(item => item.AllocatedCompartment).Include(item => item.Cancellation);

    private async Task<ReturnRequest> GetValidGuestReturnAsync(Guid id, string token, CancellationToken cancellationToken)
    {
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Cancellation is not null || entity.Status != ReturnRequestStatus.Deposited || string.IsNullOrWhiteSpace(entity.ShipperSessionTokenHash))
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Pickup session is no longer valid.");
        if (!string.Equals(entity.ShipperSessionTokenHash, tokenHashService.HashToken(token.Trim()), StringComparison.Ordinal))
            throw new UnauthorizedException(AuthErrorCodes.InvalidGuestSession, "Guest session token is invalid.");
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        if (entity.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes) <= timeProvider.GetUtcNow()) throw new GoneException(ReturnErrorCodes.SessionExpired, "Pickup session has expired.");
        entity.UpdatedAt = timeProvider.GetUtcNow();
        return entity;
    }

    private async Task<SystemPolicy> ActivePolicyAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return await dbContext.SystemPolicies
            .Where(item => item.IsActive && item.EffectiveFrom <= now && (!item.EffectiveTo.HasValue || item.EffectiveTo > now))
            .OrderByDescending(item => item.EffectiveFrom)
            .FirstAsync(cancellationToken);
    }

    // The pickup code is exactly six digits (SRS BR-37..39); validation and generation share these values.
    private const int PickupCodeLength = 6;
    private const int PickupCodeSpace = 1_000_000;

    private async Task<string> GeneratePickupCodeAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string code = RandomNumberGenerator.GetInt32(0, PickupCodeSpace).ToString($"D{PickupCodeLength}");
            if (!await dbContext.ReturnRequests.AnyAsync(item => item.ReturnCode == code, cancellationToken)) return code;
        }
        throw new InternalServerException("Could not generate a pickup code after multiple attempts.");
    }

    private void AddAudit(Guid? actor, string action, string entityType, Guid entityId, string details, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorUserId = actor, Action = action, EntityType = entityType, EntityId = entityId, Result = AuditLogResult.Succeeded, Details = details, OccurredAt = now });

    private static ReturnRequestResponse Map(ReturnRequest item) => new(
        item.Id, item.Cancellation is not null || item.Status != ReturnRequestStatus.Deposited ? string.Empty : item.ReturnCode,
        item.ReturnImageUrl ?? string.Empty, item.ReturnReason, item.Status, item.Locker.Code, item.Locker.Address,
        item.AllocatedCompartment?.Code, item.CreatedAt, item.ReservationExpiresAt, item.ResidentDepositedAt, item.ShipperPickedUpAt,
        item.Cancellation is null && item.Status is ReturnRequestStatus.Created or ReturnRequestStatus.Allocated or ReturnRequestStatus.Deposited,
        item.Cancellation is null ? null : new ReturnCancellationResponse(item.Cancellation.Status, item.Cancellation.RequestedAt,
            item.Cancellation.CompletedAt, false, item.Cancellation.Status switch
            {
                "Cancelled" => "Return cancelled before a compartment was allocated.",
                "AlreadyCollected" => "The shipper has already collected this return. Cancellation did not complete.",
                _ => "Cancellation requested. Web pickup is revoked, but offline code revocation and physical custody are not verified. Do not share the old code or retrieve the parcel. Contact the locker operator."
            }));
}
