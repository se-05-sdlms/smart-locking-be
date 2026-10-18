using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.SystemPolicies;

public sealed record SaveSystemPolicyRequest(
    DeliveryApprovalMode DefaultApprovalMode,
    int GuestSessionTimeoutMinutes,
    int ManualApprovalTimeoutMinutes,
    int CompartmentReservationMinutes,
    int OverdueStartAfterHours,
    decimal OverdueFeePerHour,
    string Currency,
    int MaxStorageHours,
    int ClearanceEligibilityAfterHours,
    int ClearanceNoticeBeforeHours,
    int PickupReminderStartDay,
    int OtpMaxAttempts,
    int OtpLockoutMinutes,
    bool EnableOtp,
    bool EnableRemoteUnlock,
    bool EnableFaceRecognition,
    DateTimeOffset EffectiveFrom);

public sealed record SystemPolicyResponse(
    Guid Id,
    int Version,
    DeliveryApprovalMode DefaultApprovalMode,
    int GuestSessionTimeoutMinutes,
    int ManualApprovalTimeoutMinutes,
    int CompartmentReservationMinutes,
    int OverdueStartAfterHours,
    decimal OverdueFeePerHour,
    string Currency,
    int MaxStorageHours,
    int ClearanceEligibilityAfterHours,
    int ClearanceNoticeBeforeHours,
    int PickupReminderStartDay,
    int OtpMaxAttempts,
    int OtpLockoutMinutes,
    bool EnableOtp,
    bool EnableRemoteUnlock,
    bool EnableFaceRecognition,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    DateTimeOffset CreatedAt);
