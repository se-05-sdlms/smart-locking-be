namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record ConfirmCompartmentPackageActionRequest(
    string Action,
    string Reason,
    bool SetOutOfService = false
);

public sealed record ConfirmCompartmentPackageActionResponse(
    Guid CompartmentId,
    string CompartmentCode,
    string Action,
    string? PackageCode,
    string Message
);
