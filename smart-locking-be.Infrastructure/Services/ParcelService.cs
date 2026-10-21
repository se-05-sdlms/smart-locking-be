using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ParcelService(ApplicationDbContext dbContext, TimeProvider timeProvider, ICompartmentResetService compartmentResets, IOperationsRealtimeNotifier? realtimeNotifier = null) : IParcelService
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
        if (role == nameof(UserRole.Resident))
        {
            return query.Where(parcel => parcel.DeliveryRequest.ResidentProfile!.UserId == userId);
        }

        if (role == nameof(UserRole.LockerOperator))
        {
            return query.Where(parcel => dbContext.OperatorAssignments.Any(assignment =>
                assignment.OperatorUserId == userId &&
                assignment.LockerId == parcel.DeliveryRequest.LockerId &&
                assignment.RevokedAt == null));
        }

        throw new ForbiddenException(ParcelErrorCodes.AccessDenied, "Role cannot access parcels.");
    }
    // Dùng chung use case với đường /operations để không có đường bỏ qua bước thu hồi PIN trên ESP.
    public Task<OverdueTransferResponse> TransferOverdueAsync(Guid userId, string role, Guid parcelId, CancellationToken cancellationToken = default)
        => compartmentResets.RequestTransferAsync(userId, role, parcelId, cancellationToken);

    private async Task EnsureOperationalAccessAsync(Guid userId, string role, Guid lockerId, CancellationToken cancellationToken)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role != nameof(UserRole.LockerOperator) ||
            !await dbContext.OperatorAssignments.AnyAsync(item =>
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
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.DeliveryRequest)
            .SingleOrDefaultAsync(item => item.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");

        if (parcel.Status == ParcelStatus.Retrieved)
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
