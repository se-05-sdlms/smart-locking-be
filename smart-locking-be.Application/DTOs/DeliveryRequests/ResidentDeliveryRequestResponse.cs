using System.Text.Json.Serialization;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record ResidentDeliveryRequestResponse(
    Guid RequestId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DeliveryRequestStatus Status,
    string LockerCode,
    string LockerAddress,
    string? ParcelImageUrl,
    string? RecipientPhone,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovalExpiresAt,
    DateTimeOffset? DecisionAt);
