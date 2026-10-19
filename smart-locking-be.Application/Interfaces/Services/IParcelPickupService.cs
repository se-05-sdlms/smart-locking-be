using smart_locking_be.Domain.Enums;
using smart_locking_be.Application.DTOs.Parcels;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IParcelPickupService
{
    Task<PickupUnlockResponse> UnlockAsync(
        Guid residentUserId,
        Guid parcelId,
        string? ipAddress,
        string? deviceContext,
        CancellationToken cancellationToken = default, LockerAccessMethod accessMethod = LockerAccessMethod.RemoteApp);

    Task<PickupConfirmationResponse> ConfirmPickupAsync(
        Guid residentUserId,
        Guid lockerAccessEventId,
        CancellationToken cancellationToken = default);
}
