using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ParcelPickupService(
    ApplicationDbContext dbContext,
    ILockerAccessService lockerAccessService,
    TimeProvider timeProvider) : IParcelPickupService
{
    public async Task<PickupUnlockResponse> UnlockAsync(
        Guid residentUserId,
        Guid parcelId,
        string? ipAddress,
        string? deviceContext,
        CancellationToken cancellationToken = default)
    {
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.OverdueCharge)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.ResidentProfile)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.SystemPolicy)
            .Include(item => item.DeliveryRequest).ThenInclude(request => request.Locker)
            .SingleOrDefaultAsync(item => item.Id == parcelId, cancellationToken)
            ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");

        if (parcel.DeliveryRequest.ResidentProfile?.UserId != residentUserId)
        {
            throw new ForbiddenException(ParcelErrorCodes.AccessDenied, "You cannot open this parcel's compartment.");
        }
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
        {
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel is not available for pickup.");
        }
        if (timeProvider.GetUtcNow() >= parcel.MaxStorageUntil)
        {
            throw new ConflictException(ParcelErrorCodes.StorageExpired,
                $"Parcel has exceeded its maximum storage period. Contact the collection point at {parcel.DeliveryRequest.Locker.RecoveryAddress}.");
        }
        if (parcel.OverdueCharge?.Status == OverdueChargeStatus.Outstanding)
        {
            throw new ConflictException(ParcelErrorCodes.PaymentRequired, "Pay the overdue charge before collecting this parcel.");
        }
        if (!parcel.DeliveryRequest.SystemPolicy.EnableRemoteUnlock)
        {
            throw new ConflictException(ParcelErrorCodes.RemoteUnlockDisabled, "Current policy does not allow remote locker unlock.");
        }
        if (!parcel.DeliveryRequest.AllocatedCompartmentId.HasValue)
        {
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel has no allocated locker compartment.");
        }

        OpenLockerResponse access = await lockerAccessService.OpenAsync(new OpenLockerRequest(
            parcel.DeliveryRequest.LockerId,
            parcel.DeliveryRequest.AllocatedCompartmentId.Value,
            residentUserId,
            null,
            parcel.Id,
            null,
            LockerAccessType.ResidentPickup,
            LockerAccessMethod.RemoteApp,
            ipAddress,
            deviceContext), cancellationToken);

        return new PickupUnlockResponse(
            parcel.Id,
            access.AccessEventId,
            access.Result,
            access.FailureReason,
            access.OccurredAt);
    }

    public async Task<PickupConfirmationResponse> ConfirmPickupAsync(
        Guid lockerAccessEventId,
        CancellationToken cancellationToken = default)
    {
        LockerAccessEvent accessEvent = await dbContext.LockerAccessEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == lockerAccessEventId, cancellationToken)
            ?? throw new NotFoundException(ParcelErrorCodes.InvalidAccessEvent, "Pickup unlock event not found.");

        if (accessEvent.AccessType != LockerAccessType.ResidentPickup ||
            accessEvent.Result != LockerAccessResult.Succeeded ||
            !accessEvent.ParcelId.HasValue)
        {
            throw new ConflictException(ParcelErrorCodes.InvalidAccessEvent, "Unlock event is invalid for pickup confirmation.");
        }

        await using var transaction = await ParcelTransitionLock.AcquireAsync(dbContext, accessEvent.ParcelId.Value, cancellationToken);
        Parcel parcel = await dbContext.Parcels
            .Include(item => item.DeliveryRequest)
            .SingleOrDefaultAsync(item => item.Id == accessEvent.ParcelId.Value, cancellationToken)
            ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");

        if (parcel.DeliveryRequest.AllocatedCompartmentId != accessEvent.LockerCompartmentId)
        {
            throw new ConflictException(ParcelErrorCodes.InvalidAccessEvent, "Unlock event does not match the parcel compartment.");
        }
        if (parcel.Status == ParcelStatus.Retrieved && parcel.RetrievedAt.HasValue)
        {
            return new PickupConfirmationResponse(parcel.Id, parcel.Status, parcel.RetrievedAt.Value);
        }
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
        {
            throw new ConflictException(ParcelErrorCodes.InvalidState, "Parcel cannot be confirmed as picked up in its current state.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ParcelStatus previousStatus = parcel.Status;
        parcel.Status = ParcelStatus.Retrieved;
        parcel.RetrievedAt = now;
        parcel.UpdatedAt = now;
        parcel.DeliveryRequest.CompartmentReleasedAt = now;
        parcel.DeliveryRequest.UpdatedAt = now;
        dbContext.ParcelStatusHistories.Add(new ParcelStatusHistory
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = previousStatus,
            ToStatus = ParcelStatus.Retrieved,
            Reason = "Resident collected the parcel and closed the compartment door.",
            ChangedByUserId = accessEvent.UserId,
            ChangedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new PickupConfirmationResponse(parcel.Id, parcel.Status, now);
    }
}
