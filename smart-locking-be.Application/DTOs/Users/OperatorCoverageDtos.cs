namespace smart_locking_be.Application.DTOs.Users;

public sealed record ConfirmLockerLocationRequest(double Latitude, double Longitude, string ServiceArea);
public sealed record LockerLocationResponse(Guid LockerId, double? Latitude, double? Longitude, string? ServiceArea,
    Guid? ConfirmedByUserId, DateTimeOffset? ConfirmedAt);
public sealed record CreateOperatorAbsenceRequest(Guid OperatorUserId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Reason);
public sealed record ReviewOperatorReplacementRequest(Guid? OperatorUserId, string Reason);
public sealed record OperatorAbsenceResponse(Guid Id, Guid OperatorUserId, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string Reason, DateTimeOffset? CancelledAt, IReadOnlyCollection<OperatorReplacementResponse> Proposals);
public sealed record OperatorReplacementResponse(Guid Id, Guid LockerId, Guid OriginalOperatorUserId, Guid? OperatorUserId,
    string Status, string StatusReason, Guid? AnchorLockerId, double? DistanceKm, string? ServiceArea,
    int? LoadBefore, int? LoadAfter, Guid? PolicyId, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt,
    DateTimeOffset? ActivatedAt);
