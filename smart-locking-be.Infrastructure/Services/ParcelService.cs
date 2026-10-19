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
            throw new BadRequestException("From date must not be after to date.");
        }

        if (view == ParcelListView.ClearancePending &&
            role is not (nameof(UserRole.LockerOperator) or nameof(UserRole.Administrator)))
            throw new ForbiddenException("Role cannot access the clearance queue.");

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
            _ => throw new BadRequestException("Parcel view is invalid.")
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
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Status))
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
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Status))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Parcel not found.");

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
            throw new NotFoundException("Parcel not found.");
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

        throw new ForbiddenException("Role cannot access parcels.");
    }
    public async Task<OverdueTransferResponse> TransferOverdueAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken = default)
    {
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.Locker)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.ResidentProfile)
            .SingleOrDefaultAsync(item => item.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy kiện hàng.");
        await EnsureOperationalAccessAsync(userId, role, parcel.DeliveryRequest.LockerId, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (parcel.MaxStorageUntil > now || parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new ConflictException("Kiện hàng chưa đủ điều kiện chuyển điểm tập kết.");

        ResidentProfile resident = parcel.DeliveryRequest.ResidentProfile
            ?? throw new ConflictException("Kiện hàng không có cư dân nhận.");
        parcel.Status = ParcelStatus.Removed;
        parcel.RemovedAt = now;
        parcel.RemovedByUserId = userId;
        parcel.RemovalReason = "Transferred to overdue collection point";
        parcel.UpdatedAt = now;
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
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
        await PublishAsync(parcel, now, cancellationToken);
        return new OverdueTransferResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.DeliveryRequest.Locker.RecoveryAddress,
            parcel.Status.ToString());
    }

    private async Task EnsureOperationalAccessAsync(Guid userId, string role, Guid lockerId, CancellationToken cancellationToken)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role != nameof(UserRole.LockerOperator) ||
            !await dbContext.OperatorAssignments.AnyAsync(item =>
                item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
                cancellationToken))
            throw new ForbiddenException("Tủ không thuộc phạm vi vận hành.");
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
            ?? throw new NotFoundException("Không tìm thấy bưu kiện.");

        if (parcel.Status == ParcelStatus.Retrieved)
            return;
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new ConflictException("Bưu kiện không còn ở trạng thái có thể hoàn tất lấy hàng.");

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
            Reason = "Cửa ngăn đã đóng sau khi người nhận lấy bưu kiện.",
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
