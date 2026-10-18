using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.DTOs.Payments;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IResidentSubscriptionService
{
    Task<SubscriptionCheckoutResponse> SubscribeAsync(Guid userId, SubscribePlanRequest request, CancellationToken cancellationToken = default);
    Task ActivateSubscriptionAsync(PayOSWebhookData payment, CancellationToken cancellationToken = default);
    Task<ResidentSubscriptionResponse?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResidentSubscriptionResponse>> GetHistoryAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> ExpireOutdatedSubscriptionsAsync(CancellationToken cancellationToken = default);
}
