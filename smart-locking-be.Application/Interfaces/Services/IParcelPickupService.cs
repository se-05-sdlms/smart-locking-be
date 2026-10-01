using smart_locking_be.Application.DTOs.Parcels;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IParcelPickupService
{
    Task<PickupUnlockResponse> UnlockAsync(
        Guid residentUserId,
        Guid parcelId,
        string? ipAddress,
        string? deviceContext,
        CancellationToken cancellationToken = default);

    Task<PickupConfirmationResponse> ConfirmPickupAsync(
        Guid lockerAccessEventId,
        CancellationToken cancellationToken = default);
}
