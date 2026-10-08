using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Residents;

public sealed record UpdateResidentProfileRequest(
    string? FullName,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    DeliveryApprovalMode? DeliveryApprovalMode = null
);
