using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class AuditLogService(ApplicationDbContext dbContext) : IAuditLogService
{
    public async Task<PagedResult<AuditLogResponse>> GetAsync(
        string? query, Guid? actorUserId, string? action, string? entityType, Guid? entityId,
        DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default, int pageNumber = 1, int pageSize = 20)
    {
        IQueryable<AuditLog> source = dbContext.AuditLogs.Include(item => item.ActorUser).AsNoTracking();
        string term = query?.Trim() ?? string.Empty;
        if (from.HasValue) source = source.Where(item => item.OccurredAt >= from);
        if (to.HasValue) source = source.Where(item => item.OccurredAt < to);
        if (actorUserId.HasValue) source = source.Where(item => item.ActorUserId == actorUserId);
        if (!string.IsNullOrWhiteSpace(action)) source = source.Where(item => item.Action == action.Trim());
        if (!string.IsNullOrWhiteSpace(entityType)) source = source.Where(item => item.EntityType == entityType.Trim());
        if (entityId.HasValue) source = source.Where(item => item.EntityId == entityId);
        if (term.Length > 0)
            source = source.Where(item => item.Action.Contains(term) || (item.Details != null && item.Details.Contains(term)));
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await source.CountAsync(cancellationToken);
        List<AuditLogResponse> items = await source.OrderByDescending(item => item.OccurredAt)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(item => new AuditLogResponse(
                item.Id,
                item.ActorUserId,
                item.ActorUser == null ? "System/Guest" : item.ActorUser.Email ?? item.ActorUser.PhoneNumber ?? item.ActorUser.Id.ToString(),
                item.Action, item.EntityType, item.EntityId, item.Result, item.IpAddress, item.Details, item.OccurredAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditLogResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<AuditLogResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogs.AsNoTracking().Include(item => item.ActorUser)
            .Where(item => item.Id == id)
            .Select(item => new AuditLogResponse(
                item.Id,
                item.ActorUserId,
                item.ActorUser == null ? "System/Guest" : item.ActorUser.Email ?? item.ActorUser.PhoneNumber ?? item.ActorUser.Id.ToString(),
                item.Action, item.EntityType, item.EntityId, item.Result, item.IpAddress, item.Details, item.OccurredAt))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Audit log not found.");
}
