using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record OpenLockerRequest(
    Guid LockerId,
    Guid LockerCompartmentId,
    Guid? UserId,
    Guid? DeliveryRequestId,
    Guid? ParcelId,
    Guid? ReturnRequestId,
    LockerAccessType AccessType,
    LockerAccessMethod AccessMethod,
    string? IpAddress,
    string? DeviceContext);

public sealed record OpenLockerResponse(
    Guid AccessEventId,
    Guid LockerId,
    Guid LockerCompartmentId,
    string DeviceIdentifier,
    int HardwareChannel,
    LockerAccessResult Result,
    string? FailureReason,
    DateTimeOffset OccurredAt);

public sealed record LockerUnlockCommand(
    Guid CommandId,
    string DeviceIdentifier,
    int HardwareChannel);

public sealed record LockerPinConfigCommand(
    Guid CommandId,
    string DeviceIdentifier,
    int HardwareChannel,
    string PinCode);

public sealed record ConfigureCompartmentPinRequest(
    string PinCode);

public sealed record ConfigureCompartmentPinResponse(
    Guid LockerId,
    Guid LockerCompartmentId,
    string DeviceIdentifier,
    int HardwareChannel,
    string PinCode,
    bool Dispatched,
    DateTimeOffset ConfiguredAt);

public sealed record SyncOfflineAccessItem(
    string DeviceIdentifier,
    int HardwareChannel,
    string PinUsed,
    DateTimeOffset OccurredAt,
    string? AccessMethod = "Bluetooth");

public sealed record SyncOfflineAccessRequest(
    List<SyncOfflineAccessItem> Events);

public sealed record SyncOfflineAccessResponse(
    int TotalProcessed,
    int SuccessCount,
    List<string> Details);
