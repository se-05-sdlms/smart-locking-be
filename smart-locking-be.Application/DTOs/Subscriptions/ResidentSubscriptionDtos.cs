namespace smart_locking_be.Application.DTOs.Subscriptions;

public sealed record ResidentSubscriptionResponse(Guid Id, Guid PlanId, string PlanName, DateTimeOffset StartDate, DateTimeOffset EndDate, string Status, decimal Price, string? ExternalOrderCode, DateTimeOffset? PaidAt,
    string? PlanCode = null, int? DurationDays = null, int? MaxActiveParcels = null, bool RequiresBenefitsReconciliation = false);
public sealed record SubscribePlanRequest(Guid PlanId);
/// <summary>
/// Activated = true for a free plan that took effect immediately; CheckoutUrl is empty and OrderCode/Amount are 0 then.
/// </summary>
public sealed record SubscriptionCheckoutResponse(Guid SubscriptionId, string CheckoutUrl, long OrderCode, decimal Amount, bool Activated = false);
