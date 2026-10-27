using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class MaintenanceService(ApplicationDbContext dbContext, TimeProvider timeProvider) : IMaintenanceService
{
    public async Task<PagedResult<MaintenanceResponse>> GetAsync(
        Guid userId, string role, Guid? lockerId = null, Guid? compartmentId = null,
        MaintenanceStatus? status = null, MaintenancePriority? priority = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken cancellationToken = default, int pageNumber = 1, int pageSize = 20)
    {
        IQueryable<MaintenanceRequest> query = Scope(userId, role)
            .Include(item => item.Locker)
            .Include(item => item.LockerCompartment);
        if (lockerId.HasValue) query = query.Where(item => item.LockerId == lockerId);
        if (compartmentId.HasValue) query = query.Where(item => item.LockerCompartmentId == compartmentId);
        if (status.HasValue) query = query.Where(item => item.Status == status);
        if (priority.HasValue) query = query.Where(item => item.Priority == priority);
        if (from.HasValue) query = query.Where(item => item.CreatedAt >= from);
        if (to.HasValue) query = query.Where(item => item.CreatedAt < to);
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await query.CountAsync(cancellationToken);
        MaintenanceResponse[] items = (await query.OrderByDescending(item => item.UpdatedAt)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken))
            .Select(Map).ToArray();
        return new PagedResult<MaintenanceResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<MaintenanceDetailResponse> GetByIdAsync(
        Guid userId, string role, Guid id, CancellationToken cancellationToken = default)
    {
        MaintenanceRequest item = await Scope(userId, role)
            .Include(candidate => candidate.Locker)
            .Include(candidate => candidate.LockerCompartment)
            .Include(candidate => candidate.Activities)
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new NotFoundException(MaintenanceErrorCodes.NotFound, "Maintenance request not found.");
        return new MaintenanceDetailResponse(Map(item), item.Activities
            .OrderBy(activity => activity.CreatedAt)
            .Select(activity => new MaintenanceActivityResponse(
                activity.Id, activity.ActionByUserId, activity.ActionType,
                activity.FromStatus, activity.ToStatus, activity.Notes, activity.CreatedAt))
            .ToList());
    }

    public async Task<MaintenanceResponse> CreateAsync(Guid userId, string role, CreateMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(userId, role, request.LockerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Description))
            throw BadRequestException.For("description", "Maintenance description is required.");
        if (request.CompartmentId.HasValue && !await dbContext.LockerCompartments.AnyAsync(
            item => item.Id == request.CompartmentId && item.LockerId == request.LockerId,
            cancellationToken))
            throw new NotFoundException(LockerErrorCodes.InvalidCompartment, "Locker compartment not found.");
        if (request.IncidentId.HasValue && !await dbContext.Incidents.AnyAsync(item =>
            item.Id == request.IncidentId && item.LockerId == request.LockerId &&
            item.LockerCompartmentId == request.CompartmentId, cancellationToken))
            throw new BadRequestException(MaintenanceErrorCodes.InvalidRequest, "Incident does not match the locker or compartment under maintenance.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        MaintenanceRequest entity = new()
        {
            Id = Guid.NewGuid(),
            LockerId = request.LockerId,
            LockerCompartmentId = request.CompartmentId,
            CreatedByUserId = userId,
            IncidentId = request.IncidentId,
            Priority = request.Priority,
            Status = MaintenanceStatus.Open,
            Description = request.Description.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        entity.Activities.Add(new MaintenanceActivity
        {
            Id = Guid.NewGuid(),
            ActionByUserId = userId,
            ActionType = "Created",
            ToStatus = MaintenanceStatus.Open,
            CreatedAt = now
        });
        dbContext.MaintenanceRequests.Add(entity);
        AddAudit(userId, "Maintenance.Created", entity.Id, entity.Description, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAsync(entity.Id, cancellationToken);
    }

    public async Task<MaintenanceResponse> UpdateAsync(Guid userId, string role, Guid id, UpdateMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        MaintenanceRequest entity = await dbContext.MaintenanceRequests
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(MaintenanceErrorCodes.NotFound, "Maintenance request not found.");
        await EnsureAccessAsync(userId, role, entity.LockerId, cancellationToken);
        if (request.Status == MaintenanceStatus.Closed && string.IsNullOrWhiteSpace(request.ResolutionSummary))
            throw BadRequestException.For("result", "Maintenance result is required when closing a request.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        MaintenanceStatus previous = entity.Status;
        entity.Status = request.Status;
        entity.ResolutionSummary = request.ResolutionSummary?.Trim() ?? entity.ResolutionSummary;
        entity.UpdatedAt = now;
        entity.ClosedAt = request.Status == MaintenanceStatus.Closed ? now : null;
        dbContext.MaintenanceActivities.Add(new MaintenanceActivity
        {
            Id = Guid.NewGuid(),
            MaintenanceRequestId = id,
            ActionByUserId = userId,
            ActionType = "StatusChanged",
            FromStatus = previous,
            ToStatus = request.Status,
            Notes = request.Notes?.Trim(),
            CreatedAt = now
        });
        AddAudit(userId, "Maintenance.StatusChanged", id, $"{previous} -> {request.Status}", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAsync(id, cancellationToken);
    }

    private IQueryable<MaintenanceRequest> Scope(Guid userId, string role)
    {
        var assignments = dbContext.EffectiveOperatorAssignments(timeProvider.GetUtcNow());
        return role switch
        {
            nameof(UserRole.Administrator) => dbContext.MaintenanceRequests,
            nameof(UserRole.LockerOperator) => dbContext.MaintenanceRequests.Where(item =>
                assignments.Any(assignment =>
                    assignment.OperatorUserId == userId && assignment.LockerId == item.LockerId && assignment.RevokedAt == null)),
            _ => throw new ForbiddenException(LockerErrorCodes.AccessDenied, "Locker is outside your operational scope.")
        };
    }

    private async Task EnsureAccessAsync(Guid userId, string role, Guid lockerId, CancellationToken cancellationToken)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role != nameof(UserRole.LockerOperator) || !await dbContext.EffectiveOperatorAssignments(timeProvider.GetUtcNow()).AnyAsync(item =>
            item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
            cancellationToken))
            throw new ForbiddenException(LockerErrorCodes.AccessDenied, "Locker is outside your operational scope.");
    }

    private void AddAudit(Guid userId, string action, Guid id, string details, DateTimeOffset now) =>
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(MaintenanceRequest),
            EntityId = id,
            Result = AuditLogResult.Succeeded,
            Details = details,
            OccurredAt = now
        });

    private async Task<MaintenanceResponse> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        Map(await dbContext.MaintenanceRequests.Include(item => item.Locker)
            .Include(item => item.LockerCompartment).SingleAsync(item => item.Id == id, cancellationToken));

    private static MaintenanceResponse Map(MaintenanceRequest item) => new(
        item.Id, item.Locker.Code, item.LockerCompartment?.Code, item.Priority,
        item.Status, item.Description, item.ResolutionSummary, item.CreatedAt, item.UpdatedAt);
}
