namespace smart_locking_be.Application.DTOs.Residents;

public sealed record AccountChangeBlocker(string Kind, Guid Id, string Message);
public sealed record AccountChangeEligibilityResponse(Guid TargetLockerId, bool CanApply, IReadOnlyList<AccountChangeBlocker> Blockers);
public sealed record CreateLockerChangeRequest(Guid TargetLockerId, string Reason);
public sealed record RequestPhoneChangeOtpRequest(string NewPhoneNumber, string CurrentPassword);
public sealed record CreatePhoneChangeRequest(string NewPhoneNumber, string CurrentPassword, string OtpCode, string Reason);
public sealed record ReviewAccountChangeRequest(bool Approve, string Reason);
public sealed record AccountChangeEventResponse(Guid Id, string Action, string Message, DateTimeOffset CreatedAt);
public sealed record ResidentAccountChangeResponse(Guid Id, Guid ResidentUserId, string Kind, string Status,
    string Reason, Guid? OriginalLockerId, Guid? TargetLockerId, string? TargetLockerCode,
    string? OriginalPhoneNumber, string? NewPhoneNumber, DateTimeOffset? PhoneVerificationExpiresAt,
    string? ReviewReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? AppliedAt,
    IReadOnlyList<AccountChangeBlocker> Blockers, IReadOnlyList<AccountChangeEventResponse> History);
