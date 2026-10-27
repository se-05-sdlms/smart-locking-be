using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Returns;

public sealed record CreateReturnRequest(string ImageUrl, string? Note, string? DeviceIdentifier = null);
public sealed record ReturnUnlockResponse(Guid ReturnRequestId, string CompartmentCode, Guid AccessEventId, DateTimeOffset ReservationExpiresAt, BluetoothCommandResponse? Bluetooth = null);
public sealed record ValidateReturnPickupRequest(string LockerCode, string PickupCode);
public sealed record ReturnPickupSessionResponse(Guid ReturnRequestId, string GuestSessionToken, string LockerCode, string CompartmentCode, string ImageUrl, DateTimeOffset ExpiresAt, BluetoothCommandResponse? Bluetooth = null);
public sealed record ReturnRequestResponse(
    Guid Id,
    string PickupCode,
    string ImageUrl,
    string? Note,
    ReturnRequestStatus Status,
    string LockerCode,
    string LockerAddress,
    string? CompartmentCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReservationExpiresAt,
    DateTimeOffset? ResidentDepositedAt,
    DateTimeOffset? ShipperPickedUpAt);
