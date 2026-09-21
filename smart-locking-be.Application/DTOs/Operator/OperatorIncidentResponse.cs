using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Operator;

public sealed record OperatorIncidentResponse(
    Guid Id, Guid LockerId, string LockerCode,
    Guid? CompartmentId, string? CompartmentCode,
    string Type, IncidentSource Source, IncidentStatus Status,
    string Title, string Description, Guid? AssignedOperatorUserId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
