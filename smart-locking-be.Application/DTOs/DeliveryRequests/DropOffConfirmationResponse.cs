using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record DropOffConfirmationResponse(
    Guid RequestId,
    Guid ParcelId,
    DeliveryRequestStatus Status,
    DateTimeOffset DepositedAt);
