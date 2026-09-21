namespace smart_locking_be.Application.DTOs.DeviceInstallations;

public sealed record DeviceInstallationResponse(
    Guid Id,
    string InstallationId,
    string ExpoPushToken,
    string Platform,
    bool IsActive,
    DateTimeOffset UpdatedAt);
