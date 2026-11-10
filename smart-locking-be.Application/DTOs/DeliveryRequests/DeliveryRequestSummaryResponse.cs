using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record DeliveryRequestSummaryResponse(
    Guid Id,
    DeliveryRequestStatus Status,
    string? ParcelImageUrl,
    DateTimeOffset SessionExpiresAt
);
