using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.SystemPolicies;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ISystemPolicyService
{
    Task<PagedResult<SystemPolicyResponse>> GetAsync(bool? isActive, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<SystemPolicyResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SystemPolicyResponse> CreateAsync(Guid adminUserId, SaveSystemPolicyRequest request, CancellationToken cancellationToken = default);
    Task<SystemPolicyResponse> UpdateAsync(Guid adminUserId, Guid id, SaveSystemPolicyRequest request, CancellationToken cancellationToken = default);
    Task<SystemPolicyResponse> ActivateAsync(Guid adminUserId, Guid id, CancellationToken cancellationToken = default);
}
