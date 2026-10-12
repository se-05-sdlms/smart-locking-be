using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IMaintenanceService
{
    Task<PagedResult<MaintenanceResponse>> GetAsync(Guid userId, string role, Guid? lockerId = null, Guid? compartmentId = null, MaintenanceStatus? status = null, MaintenancePriority? priority = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default, int pageNumber = 1, int pageSize = 20);
    Task<MaintenanceDetailResponse> GetByIdAsync(Guid userId, string role, Guid id, CancellationToken cancellationToken = default);
    Task<MaintenanceResponse> CreateAsync(Guid userId, string role, CreateMaintenanceRequest request, CancellationToken cancellationToken = default);
    Task<MaintenanceResponse> UpdateAsync(Guid userId, string role, Guid id, UpdateMaintenanceRequest request, CancellationToken cancellationToken = default);
}
