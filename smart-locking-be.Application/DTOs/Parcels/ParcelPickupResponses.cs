using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Domain.Enums;
using System.Text.Json.Serialization;

namespace smart_locking_be.Application.DTOs.Parcels;

public sealed record PickupUnlockResponse(
    Guid ParcelId,
    Guid AccessEventId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LockerAccessResult Result,
    string? FailureReason,
    DateTimeOffset RequestedAt,
    BluetoothCommandResponse? Bluetooth = null);

public sealed record PickupConfirmationResponse(
    Guid ParcelId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ParcelStatus Status,
    DateTimeOffset RetrievedAt);

public sealed record ConfirmPickupRequest(Guid AccessEventId);
