using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record InitiateDeliveryResponse(
    Guid Id,
    string GuestSessionToken,
    DeliveryRequestStatus Status,
    DateTimeOffset SessionExpiresAt
);
