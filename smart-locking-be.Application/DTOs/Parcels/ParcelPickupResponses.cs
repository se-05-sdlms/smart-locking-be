using smart_locking_be.Domain.Enums;
using System.Text.Json.Serialization;

namespace smart_locking_be.Application.DTOs.Parcels;

public sealed record PickupUnlockResponse(
    Guid ParcelId,
    Guid AccessEventId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LockerAccessResult Result,
    string? FailureReason,
    DateTimeOffset RequestedAt);

public sealed record PickupConfirmationResponse(
    Guid ParcelId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ParcelStatus Status,
    DateTimeOffset RetrievedAt);
