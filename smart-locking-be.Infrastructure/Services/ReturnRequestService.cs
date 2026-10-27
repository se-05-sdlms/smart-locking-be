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
        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(item => item.User).Include(item => item.RegisteredLocker)
            .SingleOrDefaultAsync(item => item.UserId == residentUserId, cancellationToken)
            ?? throw new NotFoundException(UserErrorCodes.NotFound, "Resident profile not found.");
        if (resident.User.Status != UserStatus.Active) throw new ConflictException(ReturnErrorCodes.InvalidState, "Resident account is inactive.");
        if (resident.RegisteredLocker is null || resident.RegisteredLocker.OperationalStatus != LockerOperationalStatus.Operational)
            throw new ConflictException(LockerErrorCodes.NotOperational, "Registered locker is unavailable.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        ReturnRequest entity = new()
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = resident.Id,
            LockerId = resident.RegisteredLocker.Id,
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
        entity.Locker = resident.RegisteredLocker;
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

    public async Task<ReturnUnlockResponse> OpenCompartmentAsync(Guid residentUserId, Guid id, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await ResidentQuery(residentUserId).SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Status != ReturnRequestStatus.Created) throw new ConflictException(ReturnErrorCodes.InvalidState, "Return request cannot be allocated in its current state.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        DateTimeOffset expiresAt = now.AddMinutes(policy.CompartmentReservationMinutes);
        CompartmentReservation? reservation = await compartmentAllocationService.ReserveAvailableAsync(
            entity.LockerId,
            null,
            entity.Id,
            expiresAt,
            cancellationToken);
        if (reservation is null) throw new ConflictException(LockerErrorCodes.NoAvailableCompartment, "No compartment is available in this locker.");
        LockerCompartment compartment = reservation.LockerCompartment;
        entity.AllocatedCompartmentId = compartment.Id;
        entity.AllocatedAt = now;
        entity.ReservationExpiresAt = expiresAt;
        entity.Status = ReturnRequestStatus.Allocated;
        entity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        OpenLockerResponse opened = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            entity.LockerId, compartment.Id, residentUserId, null, null, entity.Id,
            LockerAccessType.ResidentReturnDropOff, LockerAccessMethod.RemoteApp, null, "Resident Mobile"), cancellationToken);
        if (opened.Result != LockerAccessResult.Succeeded)
        {
            await compartmentAllocationService.ReleaseAsync(null, entity.Id, now, cancellationToken);
            entity.Status = ReturnRequestStatus.Failed;
            entity.FailureReason = "The locker compartment could not be opened.";
            await dbContext.SaveChangesAsync(cancellationToken);
            throw opened.Result == LockerAccessResult.Failed
                ? new ServiceUnavailableException(LockerErrorCodes.OpenFailed, entity.FailureReason)
                : new ConflictException(LockerErrorCodes.OpenFailed, entity.FailureReason);
        }
        AddAudit(residentUserId, "ReturnRequest.CompartmentOpened", nameof(ReturnRequest), entity.Id, compartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(entity, cancellationToken);
        return new ReturnUnlockResponse(entity.Id, compartment.Code, opened.AccessEventId, expiresAt);
    }

    public async Task FinalizeDepositAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
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
            $"Return parcel is stored in compartment {entity.AllocatedCompartment.Code}. Shipper pickup code: {entity.ReturnCode}.");
        AddAudit(entity.ResidentProfile.UserId, "ReturnRequest.Deposited", nameof(ReturnRequest), entity.Id, entity.AllocatedCompartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        await PublishAsync(entity, cancellationToken);
    }

    public async Task<ReturnPickupSessionResponse> CreateAsync(ValidateReturnPickupRequest request, CancellationToken cancellationToken = default)
    {
        string code = request.PickupCode.Trim();
        if (code.Length != PickupCodeLength || !code.All(char.IsAsciiDigit)) throw BadRequestException.For("pickupCode", "Pickup code must contain exactly six digits.");
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.ReturnCode == code && item.Locker.Code == request.LockerCode.Trim(), cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.InvalidPickupCode, "Pickup code is invalid for this locker.");
        if (entity.Status != ReturnRequestStatus.Deposited || entity.AllocatedCompartment is null)
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Pickup code has been used or is not ready.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        string token = tokenHashService.CreateSecureToken();
        entity.ShipperSessionTokenHash = tokenHashService.HashToken(token);
        entity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, token, entity.Locker.Code, entity.AllocatedCompartment.Code, entity.ReturnImageUrl!, now.AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task<ReturnPickupSessionResponse> GetAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await GetValidGuestReturnAsync(id, guestSessionToken, cancellationToken);
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, guestSessionToken, entity.Locker.Code, entity.AllocatedCompartment!.Code, entity.ReturnImageUrl!, entity.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task<ReturnPickupSessionResponse> OpenCompartmentAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await GetValidGuestReturnAsync(id, guestSessionToken, cancellationToken);
        OpenLockerResponse opened = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            entity.LockerId, entity.AllocatedCompartmentId!.Value, null, null, null, entity.Id,
            LockerAccessType.ShipperReturnPickup, LockerAccessMethod.GuestSession, null, "Shipper Guest Web"), cancellationToken);
        if (opened.Result != LockerAccessResult.Succeeded)
            throw opened.Result == LockerAccessResult.Failed
                ? new ServiceUnavailableException(LockerErrorCodes.OpenFailed, "The locker compartment could not be opened.")
                : new ConflictException(LockerErrorCodes.OpenFailed, "The locker compartment could not be opened.");
        SystemPolicy policy = await ActivePolicyAsync(cancellationToken);
        return new ReturnPickupSessionResponse(entity.Id, guestSessionToken, entity.Locker.Code, entity.AllocatedCompartment!.Code, entity.ReturnImageUrl!, timeProvider.GetUtcNow().AddMinutes(policy.GuestSessionTimeoutMinutes));
    }

    public async Task FinalizePickupAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Status == ReturnRequestStatus.Completed) return;
        if (entity.Status != ReturnRequestStatus.Deposited || entity.AllocatedCompartment is null)
            throw new ConflictException(ReturnErrorCodes.InvalidState, "Return request cannot complete pickup in its current state.");
        DateTimeOffset now = completedAt;
        entity.Status = ReturnRequestStatus.Completed;
        entity.ShipperPickedUpAt = now;
        entity.CompartmentReleasedAt = now;
        entity.ShipperSessionTokenHash = null;
        entity.UpdatedAt = now;
        Guid pushId = pushNotificationService.EnqueueReturnNotification(
            entity.ResidentProfile.UserId, entity.Id, "ReturnPickedUp", "Shipper collected return parcel",
            $"Shipper collected the return parcel from locker {entity.Locker.Code}, compartment {entity.AllocatedCompartment.Code}.");
        AddAudit(null, "ReturnRequest.PickedUp", nameof(ReturnRequest), entity.Id, entity.AllocatedCompartment.Code, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        await PublishAsync(entity, cancellationToken);
    }

    public async Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ReturnRequest> items = await dbContext.ReturnRequests
            .Where(item => item.Status == ReturnRequestStatus.Allocated && item.ReservationExpiresAt <= now)
            .ToListAsync(cancellationToken);
        foreach (ReturnRequest item in items)
        {
            item.Status = ReturnRequestStatus.Expired; item.UpdatedAt = now; item.CompartmentReleasedAt = now;
            await compartmentAllocationService.ReleaseAsync(null, item.Id, now, cancellationToken);
        }
        if (items.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (ReturnRequest item in items)
                await PublishAsync(item, cancellationToken);
        }
        return items.Count;
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
        .Include(item => item.Locker).Include(item => item.AllocatedCompartment);

    private async Task<ReturnRequest> GetValidGuestReturnAsync(Guid id, string token, CancellationToken cancellationToken)
    {
        ReturnRequest entity = await FullQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
        if (entity.Status != ReturnRequestStatus.Deposited || string.IsNullOrWhiteSpace(entity.ShipperSessionTokenHash))
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
            if (!await dbContext.ReturnRequests.AnyAsync(item => item.ReturnCode == code && item.Status != ReturnRequestStatus.Completed, cancellationToken)) return code;
        }
        throw new InternalServerException("Could not generate a pickup code after multiple attempts.");
    }

    private void AddAudit(Guid? actor, string action, string entityType, Guid entityId, string details, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorUserId = actor, Action = action, EntityType = entityType, EntityId = entityId, Result = AuditLogResult.Succeeded, Details = details, OccurredAt = now });

    private static ReturnRequestResponse Map(ReturnRequest item) => new(
        item.Id, item.Status == ReturnRequestStatus.Created || item.Status == ReturnRequestStatus.Allocated ? string.Empty : item.ReturnCode,
        item.ReturnImageUrl!, item.ReturnReason, item.Status, item.Locker.Code, item.Locker.Address,
        item.AllocatedCompartment?.Code, item.CreatedAt, item.ReservationExpiresAt, item.ResidentDepositedAt, item.ShipperPickedUpAt);
}
