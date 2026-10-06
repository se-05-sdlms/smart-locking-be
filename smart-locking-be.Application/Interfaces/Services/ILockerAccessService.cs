using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface ILockerAccessService
{
    Task<OpenLockerResponse> OpenAsync(
        OpenLockerRequest request,
        CancellationToken cancellationToken = default);

    Task<SyncOfflineAccessResponse> SyncOfflineEventsAsync(
        SyncOfflineAccessRequest request,
        CancellationToken cancellationToken = default);

    Task<ConfigureCompartmentPinResponse> ConfigurePinAsync(
        Guid lockerId,
        Guid compartmentId,
        ConfigureCompartmentPinRequest request,
        CancellationToken cancellationToken = default);
}
