namespace smart_locking_be.Application.DTOs.Subscriptions;

public sealed record ResidentSubscriptionResponse(Guid Id, Guid PlanId, string PlanName, DateTimeOffset StartDate, DateTimeOffset EndDate, string Status, decimal Price, string? ExternalOrderCode, DateTimeOffset? PaidAt);
public sealed record SubscribePlanRequest(Guid PlanId);
public sealed record SubscriptionCheckoutResponse(Guid SubscriptionId, string CheckoutUrl, long OrderCode, decimal Amount);
