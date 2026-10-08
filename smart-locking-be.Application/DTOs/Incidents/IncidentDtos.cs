using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Incidents;

public sealed record CreateIncidentRequest(
    string Type,
    string Title,
    string Description,
    Guid? ParcelId = null,
    Guid? ReturnRequestId = null,
    Guid? PaymentTransactionId = null,
    Guid? LockerId = null,
    Guid? LockerCompartmentId = null,
    string? EvidenceUrl = null);

public sealed record CreateGuestIncidentRequest(
    string LockerCode,
    string Type,
    string Title,
    string Description,
    Guid? DeliveryRequestId = null,
    Guid? ReturnRequestId = null,
    Guid? LockerCompartmentId = null);

public sealed record GuestIncidentResponse(
    Guid Id,
    string ReferenceCode,
    IncidentStatus Status,
    DateTimeOffset CreatedAt);

public sealed record UpdateIncidentStatusRequest(
    IncidentStatus Status,
    string? Notes = null,
    string? ResolutionSummary = null);

public sealed record AddIncidentActionRequest(string Notes);

public sealed record IncidentActionResponse(
    Guid Id,
    Guid ActionByUserId,
    string ActionByName,
    string ActionType,
    IncidentStatus? FromStatus,
    IncidentStatus? ToStatus,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record IncidentListItemResponse(
    Guid Id,
    string Type,
    IncidentSource Source,
    IncidentStatus Status,
    string Title,
    Guid LockerId,
    string LockerCode,
    string LockerAddress,
    Guid? ParcelId,
    string? ParcelCode,
    Guid? ReturnRequestId,
    string? ReturnCode,
    Guid? PaymentTransactionId,
    Guid? AssignedOperatorUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record IncidentDetailResponse(
    Guid Id,
    string Type,
    IncidentSource Source,
    IncidentStatus Status,
    string Title,
    string Description,
    string? EvidenceUrl,
    Guid LockerId,
    string LockerCode,
    string LockerAddress,
    Guid? LockerCompartmentId,
    string? LockerCompartmentCode,
    Guid? ParcelId,
    string? ParcelCode,
    Guid? ReturnRequestId,
    string? ReturnCode,
    Guid? PaymentTransactionId,
    Guid? AssignedOperatorUserId,
    string? AssignedOperatorName,
    string? ResolutionSummary,
    DateTimeOffset? EscalatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<IncidentActionResponse> Actions);
