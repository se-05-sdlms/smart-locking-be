using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class SystemPolicy
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public DeliveryApprovalMode DefaultApprovalMode { get; set; }
    public int DeliveryRequestExpiryMinutes { get; set; }
    public int OverdueStartAfterHours { get; set; }
    public decimal OverdueFeePerHour { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int MaxStorageHours { get; set; }
    public int ClearanceEligibilityAfterHours { get; set; }
    public int ClearanceNoticeBeforeHours { get; set; }
    public bool EnablePersonalQr { get; set; }
    public bool EnableOtp { get; set; }
    public bool EnableRemoteUnlock { get; set; }
    public bool EnableFaceRecognition { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User CreatedByUser { get; set; } = null!;
    public ICollection<NotificationRule> NotificationRules { get; set; } = new List<NotificationRule>();
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();
}
