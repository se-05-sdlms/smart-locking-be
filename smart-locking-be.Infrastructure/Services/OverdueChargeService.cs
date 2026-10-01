using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OverdueChargeService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : IOverdueChargeService
{
    public async Task<int> CalculateAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Parcel> parcels = await dbContext.Parcels
            .Include(parcel => parcel.DeliveryRequest)
                .ThenInclude(request => request.SystemPolicy)
            .Include(parcel => parcel.OverdueCharge)
            .Where(parcel =>
                (parcel.Status == ParcelStatus.Stored || parcel.Status == ParcelStatus.Overdue) &&
                parcel.PickupDueAt < now)
            .ToListAsync(cancellationToken);

        int processed = 0;
        foreach (Parcel parcel in parcels)
        {
            SystemPolicy policy = parcel.DeliveryRequest.SystemPolicy;
            if (policy.OverdueFeePerHour < 0 || string.IsNullOrWhiteSpace(policy.Currency))
            {
                continue;
            }

            if (parcel.Status == ParcelStatus.Stored)
            {
                parcel.Status = ParcelStatus.Overdue;
                parcel.UpdatedAt = now;
                dbContext.ParcelStatusHistories.Add(new ParcelStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ParcelId = parcel.Id,
                    FromStatus = ParcelStatus.Stored,
                    ToStatus = ParcelStatus.Overdue,
                    Reason = "Free storage period expired.",
                    ChangedAt = now,
                });
            }

            if (policy.OverdueFeePerHour > 0 && parcel.OverdueCharge?.Status != OverdueChargeStatus.Paid)
            {
                int billableHours = (int)Math.Ceiling((now - parcel.PickupDueAt).TotalHours);
                if (parcel.OverdueCharge is null)
                {
                    OverdueCharge charge = new()
                    {
                        Id = Guid.NewGuid(),
                        ParcelId = parcel.Id,
                        Amount = billableHours * policy.OverdueFeePerHour,
                        RatePerHourSnapshot = policy.OverdueFeePerHour,
                        Currency = policy.Currency.ToUpperInvariant(),
                        ChargeStartAt = parcel.PickupDueAt,
                        CalculatedThrough = now,
                        Status = OverdueChargeStatus.Outstanding,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    parcel.OverdueCharge = charge;
                    dbContext.OverdueCharges.Add(charge);
                }
                else
                {
                    parcel.OverdueCharge.Amount = billableHours * parcel.OverdueCharge.RatePerHourSnapshot;
                    parcel.OverdueCharge.CalculatedThrough = now;
                    parcel.OverdueCharge.UpdatedAt = now;
                }
            }

            processed++;
        }

        if (processed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return processed;
    }
}
