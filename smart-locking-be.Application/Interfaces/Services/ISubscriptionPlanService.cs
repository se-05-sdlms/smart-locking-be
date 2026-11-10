using smart_locking_be.Application.DTOs.Subscriptions;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ISubscriptionPlanService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanResponse> CreateAsync(CreateSubscriptionPlanRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanResponse> UpdateAsync(Guid id, UpdateSubscriptionPlanRequest request, CancellationToken cancellationToken = default);
}
