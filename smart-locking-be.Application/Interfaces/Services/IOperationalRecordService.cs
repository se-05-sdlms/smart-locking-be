using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOperationalRecordService
{
    Task<PagedResult<OperationalRecordResponse>> SearchAsync(
        Guid userId,
        string role,
        string? query,
        Guid? lockerId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);
}
