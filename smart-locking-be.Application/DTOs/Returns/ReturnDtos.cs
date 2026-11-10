using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Returns;

public sealed record CreateReturnRequest(string ImageUrl, string? Note);
public sealed record ReturnUnlockResponse(Guid ReturnRequestId, string CompartmentCode, Guid AccessEventId, DateTimeOffset ReservationExpiresAt);
public sealed record ValidateReturnPickupRequest(string LockerCode, string PickupCode);
public sealed record ReturnPickupSessionResponse(Guid ReturnRequestId, string GuestSessionToken, string LockerCode, string CompartmentCode, string ImageUrl, DateTimeOffset ExpiresAt, bool Completed = false);
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
    DateTimeOffset? ShipperPickedUpAt,
    bool CanCancel = false,
    ReturnCancellationResponse? Cancellation = null);

public sealed record ReturnCancellationResponse(string Status, DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, bool CanRetrieve, string Message);
