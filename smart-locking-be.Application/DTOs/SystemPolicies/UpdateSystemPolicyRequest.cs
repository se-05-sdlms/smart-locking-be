using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.SystemPolicies;

public sealed record UpdateSystemPolicyRequest(
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
    int OtpMaxAttempts,
    int OtpLockoutMinutes,
    bool EnableOtp,
    bool EnableRemoteUnlock,
    bool EnableFaceRecognition,
    int? ExpectedVersion = null,
    IReadOnlyList<UpdateNotificationRuleRequest>? NotificationRules = null
);
