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
        CancellationToken cancellationToken = default, LockerAccessMethod accessMethod = LockerAccessMethod.RemoteApp)
    {
        Parcel parcel = await ParcelRetrievalEligibility.LoadAsync(dbContext, residentUserId, parcelId, timeProvider.GetUtcNow(), cancellationToken);
        bool allowed = accessMethod == LockerAccessMethod.Bluetooth
            ? await dbContext.SystemPolicies.AnyAsync(x => x.IsActive && x.EnableBluetoothUnlock && x.EffectiveFrom <= timeProvider.GetUtcNow() &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo > timeProvider.GetUtcNow()), cancellationToken)
            : parcel.DeliveryRequest.SystemPolicy.EnableRemoteUnlock;
        if (!allowed)
        {
            throw accessMethod == LockerAccessMethod.Bluetooth
                ? new ConflictException(BluetoothErrorCodes.Disabled, "Current policy does not allow Bluetooth unlock.")
                : new ConflictException(ParcelErrorCodes.RemoteUnlockDisabled, "Current policy does not allow remote locker unlock.");
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
            accessMethod,
            ipAddress,
            deviceContext), cancellationToken);

        return new PickupUnlockResponse(
            parcel.Id,
            access.AccessEventId,
            access.Result,
            access.FailureReason,
            access.OccurredAt, access.Bluetooth);
    }

    public async Task<PickupConfirmationResponse> ConfirmPickupAsync(
        Guid residentUserId,
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
        if (accessEvent.UserId != residentUserId)
        {
            throw new ForbiddenException(ParcelErrorCodes.AccessDenied, "Bạn không có quyền xác nhận lượt lấy hàng này.");
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
        throw new ConflictException(ParcelErrorCodes.InvalidState,
            "Chưa có nhật ký hoàn tất nhận hàng từ ESP. Cần mở, lấy hàng và đóng cửa rồi đồng bộ kết quả.");
    }
}
