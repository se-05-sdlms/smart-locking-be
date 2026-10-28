using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ParcelQuotaService(
    ApplicationDbContext dbContext,
    IOptions<ResidentSubscriptionOptions> options,
    TimeProvider timeProvider) : IParcelQuotaService
{
    public async Task<ParcelQuotaResult> CheckAsync(Guid residentProfileId, CancellationToken cancellationToken = default)
    {
        if (!options.Value.EnforceParcelLimits)
        {
            return new ParcelQuotaResult(ParcelQuotaDecision.Allowed, 0, null);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        // Stacked future periods do not count yet; only the period in effect now grants capacity.
        var entitlements = await dbContext.ResidentSubscriptions
            .AsNoTracking()
            .Where(item => item.ResidentProfileId == residentProfileId &&
                           item.Status == SubscriptionStatus.Active &&
                           item.StartDate <= now &&
                           item.EndDate > now)
            .Select(item => new { item.MaxActiveParcelsSnapshot, item.BenefitsSnapshotRecordedAt })
            .ToListAsync(cancellationToken);

        int? maxActiveParcels = entitlements
            .Where(item => item.BenefitsSnapshotRecordedAt.HasValue && item.MaxActiveParcelsSnapshot > 0)
            .Select(item => item.MaxActiveParcelsSnapshot).DefaultIfEmpty().Max();

        if (maxActiveParcels is null)
        {
            return new ParcelQuotaResult(entitlements.Count > 0
                ? ParcelQuotaDecision.BenefitsReconciliationRequired : ParcelQuotaDecision.NoActiveSubscription, 0, null);
        }

        // Parcels already in a locker plus requests that will become parcels (approved or waiting for approval).
        int stored = await dbContext.Parcels.CountAsync(
            item => item.DeliveryRequest.ResidentProfileId == residentProfileId &&
                    (item.Status == ParcelStatus.Stored || item.Status == ParcelStatus.Overdue),
            cancellationToken);
        int inFlight = await dbContext.DeliveryRequests.CountAsync(
            item => item.ResidentProfileId == residentProfileId &&
                    (item.Status == DeliveryRequestStatus.PendingApproval ||
                     item.Status == DeliveryRequestStatus.Approved ||
                     item.Status == DeliveryRequestStatus.Allocated),
            cancellationToken);
        int active = stored + inFlight;

        return new ParcelQuotaResult(
            active >= maxActiveParcels ? ParcelQuotaDecision.LimitReached : ParcelQuotaDecision.Allowed,
            active,
            maxActiveParcels);
    }
}
