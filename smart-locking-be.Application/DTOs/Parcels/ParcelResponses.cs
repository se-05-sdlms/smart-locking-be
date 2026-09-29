using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Parcels;

public enum ParcelListView
{
    Active,
    History,
    All
}

public sealed record ParcelListItemResponse(
    Guid Id,
    string ParcelCode,
    ParcelStatus Status,
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
    OverdueChargeStatus? OverdueChargeStatus);

public sealed record ParcelDetailResponse(
    Guid Id,
    string ParcelCode,
    ParcelStatus Status,
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
    OverdueChargeStatus? OverdueChargeStatus);

public sealed record ParcelStatusHistoryResponse(
    Guid Id,
    ParcelStatus? FromStatus,
    ParcelStatus ToStatus,
    string? Reason,
    DateTimeOffset ChangedAt);
