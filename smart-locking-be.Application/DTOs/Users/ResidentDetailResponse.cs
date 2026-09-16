using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record ResidentDetailResponse(
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    string? Email,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    DeliveryApprovalMode DeliveryApprovalMode,
    bool FaceRecognitionEnabled,
    DateTimeOffset? PersonalQrIssuedAt,
    UserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt
);
