using smart_locking_be.Application.DTOs.SystemPolicies;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ISystemPolicyService
{
    Task<SystemPolicyResponse> GetActivePolicyAsync(CancellationToken cancellationToken = default);

    Task<SystemPolicyResponse> UpdatePolicyAsync(
        Guid adminId,
        UpdateSystemPolicyRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
