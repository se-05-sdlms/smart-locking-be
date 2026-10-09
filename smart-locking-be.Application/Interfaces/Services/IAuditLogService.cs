using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IAuditLogService
{
    Task<PagedResult<AuditLogResponse>> GetAsync(
        string? query,
        Guid? actorUserId,
        string? action,
        string? entityType,
        Guid? entityId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);

    Task<AuditLogResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
