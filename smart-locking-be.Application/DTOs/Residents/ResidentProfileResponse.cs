using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Residents;

public sealed record ResidentProfileResponse(
    Guid Id,
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    string? Email,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    DeliveryApprovalMode DeliveryApprovalMode,
    bool FaceRecognitionEnabled,
    DateTimeOffset PersonalQrIssuedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
