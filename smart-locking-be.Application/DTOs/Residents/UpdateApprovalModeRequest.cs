using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Residents;

public sealed record UpdateApprovalModeRequest(
    DeliveryApprovalMode DeliveryApprovalMode
);
