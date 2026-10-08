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
        Guid userId, string role, string? query, Guid? lockerId,
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
        OperationalRecordResponse[] ordered = result.OrderByDescending(item => item.OccurredAt).ToArray();
        return new PagedResult<OperationalRecordResponse>(
            ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray(),
            ordered.Length, pageNumber, pageSize);
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
