using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Operations;

public sealed record EmergencyUnlockRequest(Guid LockerId, Guid CompartmentId, Guid? IncidentId, string Reason);

public sealed record EmergencyUnlockResponse(Guid Id, EmergencyUnlockResult Result, string CompartmentCode, DateTimeOffset RequestedAt);

public sealed record CreateMaintenanceRequest(Guid LockerId, Guid? CompartmentId, Guid? IncidentId, MaintenancePriority Priority, string Description);

public sealed record UpdateMaintenanceRequest(MaintenanceStatus Status, string? Notes, string? ResolutionSummary);

public sealed record MaintenanceResponse(Guid Id, string LockerCode, string? CompartmentCode, MaintenancePriority Priority, MaintenanceStatus Status, string Description, string? ResolutionSummary, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MaintenanceActivityResponse(Guid Id, Guid ActionByUserId, string ActionType, MaintenanceStatus? FromStatus, MaintenanceStatus? ToStatus, string? Notes, DateTimeOffset CreatedAt);

public sealed record MaintenanceDetailResponse(MaintenanceResponse Request, IReadOnlyCollection<MaintenanceActivityResponse> Activities);
