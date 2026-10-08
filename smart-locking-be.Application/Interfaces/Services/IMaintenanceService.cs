using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IMaintenanceService
{
    Task<PagedResult<MaintenanceResponse>> GetAsync(Guid userId, string role, CancellationToken cancellationToken = default, int pageNumber = 1, int pageSize = 20);
    Task<MaintenanceResponse> CreateAsync(Guid userId, string role, CreateMaintenanceRequest request, CancellationToken cancellationToken = default);
    Task<MaintenanceResponse> UpdateAsync(Guid userId, string role, Guid id, UpdateMaintenanceRequest request, CancellationToken cancellationToken = default);
}
