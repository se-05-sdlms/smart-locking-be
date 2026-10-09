using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OperationalRecordService(ApplicationDbContext dbContext) : IOperationalRecordService
{
    public async Task<PagedResult<OperationalRecordResponse>> SearchAsync(
        Guid userId, string role, string? query, Guid? lockerId, string? kind,
        DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default, int pageNumber = 1, int pageSize = 20)
    {
        HashSet<Guid> scope = (await ScopeLockers(userId, role).Select(item => item.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        if (lockerId.HasValue && !scope.Contains(lockerId.Value))
            throw new UnauthorizedAccessException("Tủ không thuộc phạm vi vận hành.");
        string term = query?.Trim() ?? string.Empty;
        IQueryable<Parcel> parcels = dbContext.Parcels.Include(item => item.DeliveryRequest).ThenInclude(item => item.Locker).Where(item => scope.Contains(item.DeliveryRequest.LockerId));
        IQueryable<ReturnRequest> returns = dbContext.ReturnRequests.Include(item => item.Locker).Where(item => scope.Contains(item.LockerId));
        IQueryable<Incident> incidents = dbContext.Incidents.Include(item => item.Locker).Where(item => item.LockerId.HasValue && scope.Contains(item.LockerId.Value));
        IQueryable<DeliveryRequest> deliveries = dbContext.DeliveryRequests.Include(item => item.Locker).Where(item => scope.Contains(item.LockerId));
        IQueryable<MaintenanceRequest> maintenance = dbContext.MaintenanceRequests.Include(item => item.Locker).Where(item => scope.Contains(item.LockerId));
        IQueryable<LockerEvent> events = dbContext.LockerEvents.Include(item => item.Locker).Where(item => scope.Contains(item.LockerId));
        IQueryable<Locker> lockers = dbContext.Lockers.Where(item => scope.Contains(item.Id));
        IQueryable<LockerCompartment> compartments = dbContext.LockerCompartments.Include(item => item.Locker).Where(item => scope.Contains(item.LockerId));
        if (lockerId.HasValue)
        {
            parcels = parcels.Where(item => item.DeliveryRequest.LockerId == lockerId);
            returns = returns.Where(item => item.LockerId == lockerId);
            incidents = incidents.Where(item => item.LockerId == lockerId);
            deliveries = deliveries.Where(item => item.LockerId == lockerId);
            maintenance = maintenance.Where(item => item.LockerId == lockerId);
            events = events.Where(item => item.LockerId == lockerId);
            lockers = lockers.Where(item => item.Id == lockerId);
            compartments = compartments.Where(item => item.LockerId == lockerId);
        }
        if (term.Length > 0)
        {
            parcels = parcels.Where(item => item.ParcelCode.Contains(term));
            returns = returns.Where(item => item.ReturnCode.Contains(term));
            incidents = incidents.Where(item => item.Title.Contains(term));
            deliveries = deliveries.Where(item =>
                (item.RecipientPhoneSnapshot != null && item.RecipientPhoneSnapshot.Contains(term)) || item.Id.ToString().Contains(term));
            maintenance = maintenance.Where(item => item.Description.Contains(term));
            events = events.Where(item =>
                (item.Details != null && item.Details.Contains(term)) || (item.Reason != null && item.Reason.Contains(term)));
            lockers = lockers.Where(item => item.Code.Contains(term) || item.Address.Contains(term));
            compartments = compartments.Where(item => item.Code.Contains(term) || item.HardwareCode.Contains(term));
        }
        List<OperationalRecordResponse> result = [];
        result.AddRange(await parcels.OrderByDescending(item => item.UpdatedAt).Take(50).Select(item => new OperationalRecordResponse("Parcel", item.Id, item.ParcelCode, item.Status.ToString(), item.DeliveryRequest.Locker.Code, "Kiện hàng cư dân", item.UpdatedAt)).ToListAsync(cancellationToken));
        result.AddRange(await returns.OrderByDescending(item => item.UpdatedAt).Take(50).Select(item => new OperationalRecordResponse("Return", item.Id, item.ReturnCode, item.Status.ToString(), item.Locker.Code, "Hàng cư dân gửi", item.UpdatedAt)).ToListAsync(cancellationToken));
        result.AddRange(await incidents.OrderByDescending(item => item.UpdatedAt).Take(50).Select(item => new OperationalRecordResponse("Incident", item.Id, item.Id.ToString(), item.Status.ToString(), item.Locker!.Code, item.Title, item.UpdatedAt)).ToListAsync(cancellationToken));
        result.AddRange(await deliveries.OrderByDescending(item => item.UpdatedAt).Take(50).Select(item => new OperationalRecordResponse("DeliveryRequest", item.Id, item.Id.ToString(), item.Status.ToString(), item.Locker.Code, item.RecipientPhoneSnapshot ?? "Yêu cầu giao hàng", item.UpdatedAt)).ToListAsync(cancellationToken));
        result.AddRange(await maintenance.OrderByDescending(item => item.UpdatedAt).Take(50).Select(item => new OperationalRecordResponse("MaintenanceRequest", item.Id, item.Id.ToString(), item.Status.ToString(), item.Locker.Code, item.Description, item.UpdatedAt)).ToListAsync(cancellationToken));
        result.AddRange(await events.OrderByDescending(item => item.OccurredAt).Take(50).Select(item => new OperationalRecordResponse("LockerEvent", item.Id, item.Id.ToString(), item.EventType.ToString(), item.Locker.Code, item.Details ?? item.Reason ?? item.EventType.ToString(), item.OccurredAt)).ToListAsync(cancellationToken));
        result.AddRange(await lockers.OrderByDescending(item => item.UpdatedAt).Take(50).Select(item => new OperationalRecordResponse("Locker", item.Id, item.Code, item.OperationalStatus.ToString(), item.Code, item.Address, item.UpdatedAt)).ToListAsync(cancellationToken));
        result.AddRange(await compartments.OrderByDescending(item => item.UpdatedAt).Take(50).Select(item => new OperationalRecordResponse("LockerCompartment", item.Id, item.Code, item.OperationalStatus.ToString(), item.Locker.Code, item.HardwareCode, item.UpdatedAt)).ToListAsync(cancellationToken));
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IEnumerable<OperationalRecordResponse> filtered = result;
        if (!string.IsNullOrWhiteSpace(kind))
            filtered = filtered.Where(item => item.Kind.Equals(kind.Trim(), StringComparison.OrdinalIgnoreCase));
        if (from.HasValue) filtered = filtered.Where(item => item.OccurredAt >= from);
        if (to.HasValue) filtered = filtered.Where(item => item.OccurredAt < to);
        OperationalRecordResponse[] ordered = filtered.OrderByDescending(item => item.OccurredAt).ToArray();
        return new PagedResult<OperationalRecordResponse>(
            ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray(),
            ordered.Length, pageNumber, pageSize);
    }

    public async Task<OperationalRecordDetailResponse> GetDetailAsync(
        Guid userId, string role, string kind, Guid id, CancellationToken cancellationToken = default)
    {
        string normalized = kind.Trim().ToLowerInvariant();
        OperationalRecordDetailResponse? result;
        switch (normalized)
        {
            case "parcel":
                {
                    Parcel? item = await dbContext.Parcels.AsNoTracking()
                        .Include(item => item.DeliveryRequest).ThenInclude(item => item.Locker)
                        .Include(item => item.DeliveryRequest).ThenInclude(item => item.AllocatedCompartment)
                        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                    result = item is null ? null : new OperationalRecordDetailResponse(
                        "Parcel", item.Id, item.ParcelCode, item.Status.ToString(),
                        item.DeliveryRequest.LockerId, item.DeliveryRequest.Locker.Code, item.UpdatedAt,
                        new Dictionary<string, string?>
                        {
                            ["compartmentCode"] = item.DeliveryRequest.AllocatedCompartment?.Code,
                            ["storedAt"] = item.StoredAt.ToString("O"),
                            ["pickupDueAt"] = item.PickupDueAt.ToString("O"),
                            ["maxStorageUntil"] = item.MaxStorageUntil.ToString("O")
                        });
                    break;
                }
            case "return":
            case "return-request":
                {
                    ReturnRequest? item = await dbContext.ReturnRequests.AsNoTracking()
                        .Include(item => item.Locker).Include(item => item.AllocatedCompartment)
                        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                    result = item is null ? null : new OperationalRecordDetailResponse(
                        "ReturnRequest", item.Id, item.ReturnCode, item.Status.ToString(), item.LockerId, item.Locker.Code,
                        item.UpdatedAt, new Dictionary<string, string?>
                        {
                            ["compartmentCode"] = item.AllocatedCompartment == null ? null : item.AllocatedCompartment.Code,
                            ["residentDepositedAt"] = item.ResidentDepositedAt.HasValue ? item.ResidentDepositedAt.Value.ToString("O") : null,
                            ["pickedUpAt"] = item.ShipperPickedUpAt.HasValue ? item.ShipperPickedUpAt.Value.ToString("O") : null
                        });
                    break;
                }
            case "deliveryrequest":
            case "delivery-request":
                {
                    DeliveryRequest? item = await dbContext.DeliveryRequests.AsNoTracking()
                        .Include(item => item.Locker).Include(item => item.AllocatedCompartment)
                        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                    result = item is null ? null : new OperationalRecordDetailResponse(
                        "DeliveryRequest", item.Id, item.Id.ToString(), item.Status.ToString(), item.LockerId, item.Locker.Code,
                        item.UpdatedAt, new Dictionary<string, string?>
                        {
                            ["recipientPhone"] = item.RecipientPhoneSnapshot,
                            ["compartmentCode"] = item.AllocatedCompartment == null ? null : item.AllocatedCompartment.Code,
                            ["depositedAt"] = item.DepositedAt.HasValue ? item.DepositedAt.Value.ToString("O") : null
                        });
                    break;
                }
            case "incident":
                {
                    Incident? item = await dbContext.Incidents.AsNoTracking().Include(item => item.Locker)
                        .SingleOrDefaultAsync(item => item.Id == id && item.LockerId.HasValue, cancellationToken);
                    result = item is null ? null : new OperationalRecordDetailResponse(
                        "Incident", item.Id, item.Id.ToString(), item.Status.ToString(), item.LockerId!.Value, item.Locker!.Code,
                        item.UpdatedAt, new Dictionary<string, string?>
                        {
                            ["type"] = item.Type,
                            ["title"] = item.Title,
                            ["description"] = item.Description,
                            ["resolution"] = item.ResolutionSummary
                        });
                    break;
                }
            case "maintenancerequest":
            case "maintenance-request":
                {
                    MaintenanceRequest? item = await dbContext.MaintenanceRequests.AsNoTracking().Include(item => item.Locker)
                        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                    result = item is null ? null : new OperationalRecordDetailResponse(
                        "MaintenanceRequest", item.Id, item.Id.ToString(), item.Status.ToString(), item.LockerId, item.Locker.Code,
                        item.UpdatedAt, new Dictionary<string, string?>
                        {
                            ["priority"] = item.Priority.ToString(),
                            ["description"] = item.Description,
                            ["resolution"] = item.ResolutionSummary
                        });
                    break;
                }
            case "lockerevent":
            case "locker-event":
                {
                    LockerEvent? item = await dbContext.LockerEvents.AsNoTracking().Include(item => item.Locker)
                        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                    result = item is null ? null : new OperationalRecordDetailResponse(
                        "LockerEvent", item.Id, item.Id.ToString(), item.EventType.ToString(), item.LockerId, item.Locker.Code,
                        item.OccurredAt, new Dictionary<string, string?>
                        {
                            ["severity"] = item.Severity.ToString(),
                            ["previousValue"] = item.PreviousValue,
                            ["newValue"] = item.NewValue,
                            ["reason"] = item.Reason,
                            ["details"] = item.Details
                        });
                    break;
                }
            default:
                throw new ArgumentException("Operational record kind is invalid.", nameof(kind));
        }
        if (result is null || !await ScopeLockers(userId, role).AnyAsync(item => item.Id == result.LockerId, cancellationToken))
            throw new KeyNotFoundException("Operational record not found.");
        return result;
    }

    private IQueryable<Locker> ScopeLockers(Guid userId, string role) => role switch
    {
        nameof(UserRole.Administrator) => dbContext.Lockers,
        nameof(UserRole.LockerOperator) => dbContext.Lockers.Where(locker =>
            dbContext.OperatorAssignments.Any(assignment =>
                assignment.OperatorUserId == userId && assignment.LockerId == locker.Id && assignment.RevokedAt == null)),
        _ => throw new UnauthorizedAccessException()
    };
}
