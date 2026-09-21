namespace smart_locking_be.Application.DTOs.DeviceInstallations;

public sealed record RegisterDeviceInstallationRequest(
    string InstallationId,
    string ExpoPushToken,
    string Platform);
