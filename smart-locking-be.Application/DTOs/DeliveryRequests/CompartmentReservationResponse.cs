namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record CompartmentReservationResponse(
    Guid RequestId,
    Guid ReservationId,
    Guid CompartmentId,
    string CompartmentCode,
    DateTimeOffset ReservedAt,
    DateTimeOffset ReservationExpiresAt);
