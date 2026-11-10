namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record PendingDeliveryRequestResponse(
    Guid RequestId,
    string LockerCode,
    string LockerAddress,
    string? ParcelImageUrl,
    string? RecipientPhone,
    DateTimeOffset CreatedAt,
    DateTimeOffset ApprovalExpiresAt);
