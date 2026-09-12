using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class ResidentProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? AvatarUrl { get; set; }
    public DeliveryApprovalMode DeliveryApprovalMode { get; set; }
    public string PersonalQrTokenHash { get; set; } = string.Empty;
    public DateTimeOffset PersonalQrIssuedAt { get; set; }
    public bool FaceRecognitionEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<ResidentBiometric> Biometrics { get; set; } = new List<ResidentBiometric>();
    public ICollection<DeliveryRequest> DeliveryRequests { get; set; } = new List<DeliveryRequest>();
}
