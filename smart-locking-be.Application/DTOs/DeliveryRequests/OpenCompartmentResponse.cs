using System.Text.Json.Serialization;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record OpenCompartmentResponse(
    Guid RequestId,
    Guid CompartmentId,
    string CompartmentCode,
    Guid AccessEventId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LockerAccessResult Result,
    string? FailureReason,
    DateTimeOffset ReservationExpiresAt);
