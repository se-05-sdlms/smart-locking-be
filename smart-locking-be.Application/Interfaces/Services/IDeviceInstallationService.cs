using smart_locking_be.Application.DTOs.DeviceInstallations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IDeviceInstallationService
{
    Task<DeviceInstallationResponse> RegisterAsync(
        Guid userId,
        RegisterDeviceInstallationRequest request,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync(
        Guid userId,
        string installationId,
        CancellationToken cancellationToken = default);
}
