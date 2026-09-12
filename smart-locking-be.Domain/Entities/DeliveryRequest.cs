using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class DeliveryRequest
{
    public Guid Id { get; set; }
    public Guid ResidentProfileId { get; set; }
    public Guid LockerClusterId { get; set; }
    public Guid SystemPolicyId { get; set; }
    public Guid? AllocatedCompartmentId { get; set; }
    public string GuestSessionTokenHash { get; set; } = string.Empty;
    public string? ShipperName { get; set; }
    public string? ShipperPhone { get; set; }
    public string RecipientPhoneSnapshot { get; set; } = string.Empty;
    public string? WaybillImageUrl { get; set; }
    public string? OcrExtractedPhone { get; set; }
    public OcrStatus? OcrStatus { get; set; }
    public string SizeCategory { get; set; } = string.Empty;
    public string? ParcelDescription { get; set; }
    public DeliveryApprovalMode ApprovalModeSnapshot { get; set; }
    public DeliveryRequestStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    public DateTimeOffset? AllocatedAt { get; set; }
    public DateTimeOffset? CompartmentReleasedAt { get; set; }
    public DeliveryRequestFailureCode? FailureCode { get; set; }
    public string? FailureDetail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ResidentProfile ResidentProfile { get; set; } = null!;
    public LockerCluster LockerCluster { get; set; } = null!;
    public SystemPolicy SystemPolicy { get; set; } = null!;
    public LockerCompartment? AllocatedCompartment { get; set; }
    public Parcel? Parcel { get; set; }
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
