using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ParcelService(ApplicationDbContext dbContext, IPushNotificationService pushNotificationService, TimeProvider timeProvider, IOperationsRealtimeNotifier? realtimeNotifier = null) : IParcelService
{
    public async Task<IReadOnlyCollection<ParcelListItemResponse>> GetParcelsAsync(
        Guid userId,
        string role,
        ParcelListView view,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (from.HasValue && to.HasValue && from > to)
        {
            throw BadRequestException.For("from", "From date must not be after to date.");
        }

        if (view == ParcelListView.ClearancePending &&
            role is not (nameof(UserRole.LockerOperator) or nameof(UserRole.Administrator)))
            throw new ForbiddenException(ParcelErrorCodes.AccessDenied, "Role cannot access the clearance queue.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        IQueryable<Parcel> query = view == ParcelListView.ClearancePending && role == nameof(UserRole.Administrator)
            ? dbContext.Parcels.AsNoTracking()
            : ScopeToUser(userId, role);
        query = view switch
        {
            ParcelListView.Active => query.Where(parcel =>
                parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue),
            ParcelListView.History => query.Where(parcel =>
                parcel.Status == ParcelStatus.Retrieved || parcel.Status == ParcelStatus.Removed),
            ParcelListView.All => query,
            ParcelListView.ClearancePending => query.Where(parcel =>
                (parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue) &&
                parcel.MaxStorageUntil <= now),
            _ => throw BadRequestException.For("view", "Parcel view is invalid.")
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLower();
            query = query.Where(parcel =>
                parcel.ParcelCode.ToLower().Contains(term) ||
                parcel.DeliveryRequest.Locker.Code.ToLower().Contains(term) ||
                parcel.DeliveryRequest.Locker.Address.ToLower().Contains(term));
        }

        if (from.HasValue)
        {
            query = query.Where(parcel => parcel.StoredAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(parcel => parcel.StoredAt <= to.Value);
        }

        return await query
            .OrderByDescending(parcel => parcel.StoredAt)
            .Select(parcel => new ParcelListItemResponse(
                parcel.Id,
                parcel.ParcelCode,
                parcel.Status,
                parcel.DeliveryRequest.LockerId,
                parcel.DeliveryRequest.Locker.Code,
                parcel.DeliveryRequest.Locker.Address,
                parcel.DeliveryRequest.AllocatedCompartmentId!.Value,
                parcel.DeliveryRequest.AllocatedCompartment!.Code,
                parcel.StoredAt,
                parcel.PickupDueAt,
                parcel.MaxStorageUntil,
                parcel.RetrievedAt,
                parcel.RemovedAt,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Amount,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Currency,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Status,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<ParcelDetailResponse> GetParcelAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken) =>
        await ScopeToUser(userId, role)
            .Where(parcel => parcel.Id == parcelId)
            .Select(parcel => new ParcelDetailResponse(
                parcel.Id,
                parcel.ParcelCode,
                parcel.Status,
                parcel.DeliveryRequest.LockerId,
                parcel.DeliveryRequest.Locker.Code,
                parcel.DeliveryRequest.Locker.Address,
                parcel.DeliveryRequest.Locker.RecoveryAddress,
                parcel.DeliveryRequest.AllocatedCompartmentId!.Value,
                parcel.DeliveryRequest.AllocatedCompartment!.Code,
                parcel.DeliveryRequest.ParcelImageUrl,
                null,
                null,
                parcel.StoredAt,
                parcel.PickupDueAt,
                parcel.MaxStorageUntil,
                parcel.RetrievedAt,
                parcel.RemovedAt,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Amount,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Currency,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Status,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Id))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");

    public async Task<IReadOnlyCollection<ParcelStatusHistoryResponse>> GetHistoryAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        bool canAccess = await ScopeToUser(userId, role)
            .AnyAsync(parcel => parcel.Id == parcelId, cancellationToken);
        if (!canAccess)
        {
            throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");
        }

        return await dbContext.ParcelStatusHistories
            .AsNoTracking()
            .Where(history => history.ParcelId == parcelId)
            .OrderByDescending(history => history.ChangedAt)
            .Select(history => new ParcelStatusHistoryResponse(
                history.Id,
                history.FromStatus,
                history.ToStatus,
                history.Reason,
                history.ChangedAt))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Parcel> ScopeToUser(Guid userId, string role)
    {
        IQueryable<Parcel> query = dbContext.Parcels.AsNoTracking();
        if (role == nameof(UserRole.Administrator)) return query;
        if (role == nameof(UserRole.Resident))
        {
            return query.Where(parcel => parcel.DeliveryRequest.ResidentProfile!.UserId == userId);
        }

        if (role == nameof(UserRole.LockerOperator))
        {
            var assignments = dbContext.EffectiveOperatorAssignments(timeProvider.GetUtcNow());
            return query.Where(parcel => assignments.Any(assignment =>
                assignment.OperatorUserId == userId &&
                assignment.LockerId == parcel.DeliveryRequest.LockerId &&
                assignment.RevokedAt == null));
        }

        throw new ForbiddenException(ParcelErrorCodes.AccessDenied, "Role cannot access parcels.");
    }
    /// <summary>Confirms the Operator has already delivered the parcel to Locker.RecoveryAddress.</summary>
    public async Task<OverdueTransferResponse> TransferOverdueAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await ParcelTransitionLock.AcquireAsync(dbContext, parcelId, cancellationToken);
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.Locker)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.ResidentProfile)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.AllocatedCompartment)
            .SingleOrDefaultAsync(item => item.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");
        await EnsureOperationalAccessAsync(userId, role, parcel.DeliveryRequest.LockerId, cancellationToken);
        if (parcel.Status == ParcelStatus.Removed && parcel.RemovedAt.HasValue &&
            parcel.RemovalReason == "Transferred to overdue collection point")
            return TransferResponse(parcel);

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (parcel.MaxStorageUntil > now || parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel is not eligible for transfer to the collection point.");
        if (string.IsNullOrWhiteSpace(parcel.DeliveryRequest.Locker.RecoveryAddress))
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Locker recovery address is not configured.");

        // A door sample is not pickup completion: OPEN telemetry can be missed and a later
        // CLOSED sample alone must not let an Operator overwrite the unresolved pickup.
        // Finalized pickup changes the parcel to Retrieved, which is already ineligible above.
        if (await dbContext.LockerAccessEvents.AnyAsync(item => item.ParcelId == parcel.Id &&
            item.AccessType == LockerAccessType.ResidentPickup && item.Result == LockerAccessResult.Succeeded, cancellationToken))
            throw new ConflictException(ParcelErrorCodes.InvalidState, "A pickup unlock has not been finalized. Reconcile it before confirming transfer.");

        ResidentProfile resident = parcel.DeliveryRequest.ResidentProfile
            ?? throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel has no recipient resident.");
        ParcelStatus previousStatus = parcel.Status;
        parcel.Status = ParcelStatus.Removed;
        parcel.RemovedAt = now;
        parcel.RemovedByUserId = userId;
        parcel.RemovalReason = "Transferred to overdue collection point";
        parcel.UpdatedAt = now;
        parcel.DeliveryRequest.CompartmentReleasedAt = now;
        parcel.DeliveryRequest.UpdatedAt = now;
        foreach (CompartmentReservation reservation in await dbContext.CompartmentReservations
            .Where(item => item.DeliveryRequestId == parcel.DeliveryRequestId && item.ReleasedAt == null)
            .ToListAsync(cancellationToken))
            reservation.ReleasedAt = now;
        dbContext.ParcelStatusHistories.Add(new ParcelStatusHistory
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = previousStatus,
            ToStatus = ParcelStatus.Removed,
            Reason = "Operator confirmed delivery to the locker recovery address.",
            ChangedByUserId = userId,
            ChangedAt = now
        });
        Guid pushId = pushNotificationService.EnqueueParcelTransferred(
            resident.UserId,
            parcel.DeliveryRequestId,
            parcel.Id,
            parcel.DeliveryRequest.Locker.RecoveryAddress);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = userId,
            Action = "Parcel.Transferred",
            EntityType = nameof(Parcel),
            EntityId = parcel.Id,
            Result = AuditLogResult.Succeeded,
            Details = parcel.DeliveryRequest.Locker.RecoveryAddress,
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        await PublishAsync(parcel, now, cancellationToken);
        return TransferResponse(parcel);
    }

    private static OverdueTransferResponse TransferResponse(Parcel parcel) => new(
        parcel.Id, parcel.ParcelCode, parcel.DeliveryRequest.Locker.RecoveryAddress, parcel.Status.ToString());

    private async Task EnsureOperationalAccessAsync(Guid userId, string role, Guid lockerId, CancellationToken cancellationToken)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role != nameof(UserRole.LockerOperator) ||
            !await dbContext.EffectiveOperatorAssignments(timeProvider.GetUtcNow()).AnyAsync(item =>
                item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
                cancellationToken))
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, "Locker is outside your operational scope.");
    }


    public async Task FinalizeRetrievalAsync(
        Guid parcelId,
        Guid? residentUserId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await ParcelTransitionLock.AcquireAsync(dbContext, parcelId, cancellationToken);
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.DeliveryRequest)
            .SingleOrDefaultAsync(item => item.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");

        // Late door telemetry must not replace an Operator-confirmed transfer with resident pickup.
        if (parcel.Status is ParcelStatus.Retrieved or ParcelStatus.Removed)
            return;
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel cannot complete pickup in its current state.");

        ParcelStatus previousStatus = parcel.Status;
        parcel.Status = ParcelStatus.Retrieved;
        parcel.RetrievedAt = completedAt;
        parcel.UpdatedAt = completedAt;
        parcel.DeliveryRequest.CompartmentReleasedAt = completedAt;
        parcel.DeliveryRequest.UpdatedAt = completedAt;
        dbContext.ParcelStatusHistories.Add(new ParcelStatusHistory
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = previousStatus,
            ToStatus = ParcelStatus.Retrieved,
            Reason = "Compartment door closed after the recipient collected the parcel.",
            ChangedByUserId = residentUserId,
            ChangedAt = completedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await PublishAsync(parcel, completedAt, cancellationToken);
    }


    private Task PublishAsync(Parcel parcel, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
        realtimeNotifier?.PublishToLockerAsync(parcel.DeliveryRequest.LockerId, new RealtimeEvent(
            "ParcelUpdated",
            parcel.Id,
            parcel.DeliveryRequest.LockerId,
            parcel.Status.ToString(),
            null,
            occurredAt), cancellationToken) ?? Task.CompletedTask;


}
