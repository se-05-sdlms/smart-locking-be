using smart_locking_be.Domain.Enums;
using System.Text.Json.Serialization;

namespace smart_locking_be.Application.DTOs.Parcels;

public sealed record OverdueTransferResponse(
    Guid ParcelId,
    string ParcelCode,
    string CollectionAddress,
    string Status);

public enum ParcelListView
{
    Active,
    History,
    All
}

public sealed record ParcelListItemResponse(
    Guid Id,
    string ParcelCode,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ParcelStatus Status,
    Guid LockerId,
    string LockerCode,
    string LockerAddress,
    Guid CompartmentId,
    string CompartmentCode,
    DateTimeOffset StoredAt,
    DateTimeOffset PickupDueAt,
    DateTimeOffset MaxStorageUntil,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset? RemovedAt,
    decimal? OverdueAmount,
    string? Currency,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OverdueChargeStatus? OverdueChargeStatus);

public sealed record ParcelDetailResponse(
    Guid Id,
    string ParcelCode,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ParcelStatus Status,
    Guid LockerId,
    string LockerCode,
    string LockerAddress,
    string LockerRecoveryAddress,
    Guid CompartmentId,
    string CompartmentCode,
    string? ParcelImageUrl,
    string? ShipperName,
    string? ShipperPhone,
    DateTimeOffset StoredAt,
    DateTimeOffset PickupDueAt,
    DateTimeOffset MaxStorageUntil,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset? RemovedAt,
    decimal? OverdueAmount,
    string? Currency,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OverdueChargeStatus? OverdueChargeStatus);

public sealed record ParcelStatusHistoryResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ParcelStatus? FromStatus,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ParcelStatus ToStatus,
    string? Reason,
    DateTimeOffset ChangedAt);
