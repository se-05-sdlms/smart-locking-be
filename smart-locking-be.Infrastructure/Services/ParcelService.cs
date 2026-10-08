using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ParcelService(
    ApplicationDbContext dbContext,
    ILockerAccessService lockerAccessService,
    IPushNotificationService pushNotificationService,
    TimeProvider timeProvider) : IParcelService
{
    public async Task<PagedResult<ParcelListItemResponse>> GetParcelsAsync(
        Guid userId,
        string role,
        ParcelListView view,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken,
        int pageNumber = 1,
        int pageSize = 20)
    {
        if (from.HasValue && to.HasValue && from > to)
        {
            throw new ArgumentException("From date must not be after to date.");
        }

        IQueryable<Parcel> query = ScopeToUser(userId, role);
        query = view switch
        {
            ParcelListView.Active => query.Where(parcel =>
                parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue),
            ParcelListView.History => query.Where(parcel =>
                parcel.Status == ParcelStatus.Retrieved || parcel.Status == ParcelStatus.Removed),
            ParcelListView.All => query,
            _ => throw new ArgumentException("Parcel view is invalid.", nameof(view))
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

        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await query.CountAsync(cancellationToken);
        List<ParcelListItemResponse> items = await query
            .OrderByDescending(parcel => parcel.StoredAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
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

        return new PagedResult<ParcelListItemResponse>(items, totalCount, pageNumber, pageSize);
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
                parcel.DeliveryRequest.ShipperName,
                parcel.DeliveryRequest.ShipperPhone,
                parcel.StoredAt,
                parcel.PickupDueAt,
                parcel.MaxStorageUntil,
                parcel.RetrievedAt,
                parcel.RemovedAt,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Amount,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Currency,
                parcel.OverdueCharge == null ? null : parcel.OverdueCharge.Status))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Parcel not found.");

    public async Task<PagedResult<ParcelStatusHistoryResponse>> GetHistoryAsync(
        Guid userId,
        string role,
        Guid parcelId,
        CancellationToken cancellationToken,
        int pageNumber = 1,
        int pageSize = 20)
    {
        bool canAccess = await ScopeToUser(userId, role)
            .AnyAsync(parcel => parcel.Id == parcelId, cancellationToken);
        if (!canAccess)
        {
            throw new KeyNotFoundException("Parcel not found.");
        }

        IQueryable<ParcelStatusHistory> query = dbContext.ParcelStatusHistories
            .AsNoTracking()
            .Where(history => history.ParcelId == parcelId);
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await query.CountAsync(cancellationToken);
        List<ParcelStatusHistoryResponse> items = await query
            .OrderByDescending(history => history.ChangedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(history => new ParcelStatusHistoryResponse(
                history.Id,
                history.FromStatus,
                history.ToStatus,
                history.Reason,
                history.ChangedAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<ParcelStatusHistoryResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PickupUnlockResponse> OpenCompartmentAsync(
        Guid residentUserId,
        Guid parcelId,
        string? ipAddress,
        string? deviceContext,
        CancellationToken cancellationToken = default)
    {
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.OverdueCharge)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.ResidentProfile)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.SystemPolicy)
            .SingleOrDefaultAsync(item => item.Id == parcelId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy bưu kiện.");

        if (parcel.DeliveryRequest.ResidentProfile?.UserId != residentUserId)
            throw new UnauthorizedAccessException("Bạn không có quyền mở ngăn chứa bưu kiện này.");
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new InvalidOperationException("Bưu kiện không còn ở trạng thái có thể nhận.");
        if (parcel.OverdueCharge?.Status == OverdueChargeStatus.Outstanding)
            throw new InvalidOperationException("Cần thanh toán phí quá hạn trước khi nhận bưu kiện.");
        if (!parcel.DeliveryRequest.SystemPolicy.EnableRemoteUnlock)
            throw new InvalidOperationException("Chính sách hiện tại không cho phép mở tủ từ ứng dụng.");
        if (!parcel.DeliveryRequest.AllocatedCompartmentId.HasValue)
            throw new InvalidOperationException("Bưu kiện chưa được gắn với ngăn locker.");

        OpenLockerResponse access = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            parcel.DeliveryRequest.LockerId,
            parcel.DeliveryRequest.AllocatedCompartmentId.Value,
            residentUserId,
            null,
            parcel.Id,
            null,
            LockerAccessType.ResidentPickup,
            LockerAccessMethod.RemoteApp,
            ipAddress,
            deviceContext), cancellationToken);

        return new PickupUnlockResponse(parcel.Id, access.AccessEventId, access.Result, access.FailureReason, access.OccurredAt);
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
            ?? throw new KeyNotFoundException("Không tìm thấy bưu kiện.");

        if (parcel.Status == ParcelStatus.Retrieved)
            return;
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new InvalidOperationException("Bưu kiện không còn ở trạng thái có thể hoàn tất lấy hàng.");

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
            ?? throw new KeyNotFoundException("Không tìm thấy kiện hàng.");
        await EnsureOperationalAccessAsync(userId, role, parcel.DeliveryRequest.LockerId, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (parcel.MaxStorageUntil > now || parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new InvalidOperationException("Kiện hàng chưa đủ điều kiện chuyển điểm tập kết.");

        ResidentProfile resident = parcel.DeliveryRequest.ResidentProfile
            ?? throw new InvalidOperationException("Kiện hàng không có cư dân nhận.");
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
            Id = Guid.NewGuid(), ActorUserId = userId, Action = "Parcel.Transferred",
            EntityType = nameof(Parcel), EntityId = parcel.Id, Result = AuditLogResult.Succeeded,
            Details = parcel.DeliveryRequest.Locker.RecoveryAddress, OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await pushNotificationService.TrySendAsync(pushId, cancellationToken);
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
            throw new UnauthorizedAccessException("Tủ không thuộc phạm vi vận hành.");
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

        throw new UnauthorizedAccessException("Role cannot access parcels.");
    }
}
