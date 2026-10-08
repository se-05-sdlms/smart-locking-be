using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IAuditLogService
{
    Task<PagedResult<AuditLogResponse>> GetAsync(
        string? query,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);
}
