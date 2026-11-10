namespace smart_locking_be.Application.DTOs.Subscriptions;

public sealed record SubscriptionPlanResponse(Guid Id, string Code, string Name, string? Description, decimal Price, int DurationDays, int MaxActiveParcels, bool IsActive);
public sealed record CreateSubscriptionPlanRequest(string Code, string Name, string? Description, decimal Price, int DurationDays, int MaxActiveParcels);
public sealed record UpdateSubscriptionPlanRequest(string Name, string? Description, decimal Price, int DurationDays, int MaxActiveParcels, bool IsActive);
