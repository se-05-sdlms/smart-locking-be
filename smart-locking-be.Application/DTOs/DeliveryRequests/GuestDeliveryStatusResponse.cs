using System.Text.Json.Serialization;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record GuestDeliveryStatusResponse(
    Guid RequestId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DeliveryRequestStatus Status,
    DateTimeOffset? ApprovalExpiresAt,
    string? CompartmentCode,
    DateTimeOffset? ReservationExpiresAt,
    Guid? ParcelId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DeliveryRequestFailureCode? FailureCode,
    string? FailureDetail);
