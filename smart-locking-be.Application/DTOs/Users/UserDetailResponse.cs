using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Users;

public sealed record UserDetailResponse(
    Guid Id,
    string FullName,
    string? PhoneNumber,
    string? Email,
    UserRole Role,
    UserStatus Status,
    bool MustChangePassword,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    DeliveryApprovalMode? DeliveryApprovalMode,
    bool FaceRecognitionEnabled,
    DateTimeOffset? PersonalQrIssuedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<OperatorAssignmentResponse> Assignments
);
