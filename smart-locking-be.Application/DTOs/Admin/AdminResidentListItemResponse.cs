using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Admin;

public sealed record AdminResidentListItemResponse(
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    string? Email,
    string? AvatarUrl,
    DeliveryApprovalMode DeliveryApprovalMode,
    UserStatus Status,
    DateTimeOffset CreatedAt
);
