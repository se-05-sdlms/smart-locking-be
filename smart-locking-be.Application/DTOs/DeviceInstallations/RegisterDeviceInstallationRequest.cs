namespace smart_locking_be.Application.DTOs.DeviceInstallations;

public sealed record RegisterDeviceInstallationRequest(
    string ExpoPushToken,
    string Platform);
