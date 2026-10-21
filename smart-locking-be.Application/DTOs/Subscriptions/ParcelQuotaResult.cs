namespace smart_locking_be.Application.DTOs.Subscriptions;

public enum ParcelQuotaDecision
{
    Allowed,
    NoActiveSubscription,
    LimitReached
}

public sealed record ParcelQuotaResult(ParcelQuotaDecision Decision, int ActiveParcels, int? MaxActiveParcels);
