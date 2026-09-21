namespace smart_locking_be.Application.DTOs.Operator;

public sealed record OverdueParcelResponse(
    Guid Id, string ParcelCode,
    Guid LockerId, string LockerCode, string LockerAddress,
    Guid? CompartmentId, string? CompartmentCode,
    DateTimeOffset StoredAt, DateTimeOffset PickupDueAt, DateTimeOffset MaxStorageUntil,
    TimeSpan OverdueDuration, bool IsThreeDaysOrMore,
    string? ResidentName, string? ResidentPhoneMasked, string RecoveryAddress,
    bool IsPastMaxStorageUntil);
