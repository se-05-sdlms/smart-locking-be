using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Operations;

public sealed record OperationalLockerResponse(Guid Id, string Code, string Address, LockerOperationalStatus Status, LockerConnectionStatus Connection, DateTimeOffset? LastSeenAt, int AvailableCompartments, int StoredParcels, int OpenIncidents);
public sealed record OperationalRecordResponse(string Kind, Guid Id, string Code, string Status, string LockerCode, string Summary, DateTimeOffset OccurredAt);
public sealed record EmergencyUnlockRequest(Guid LockerId, Guid CompartmentId, Guid? IncidentId, string Reason);
public sealed record EmergencyUnlockResponse(Guid Id, EmergencyUnlockResult Result, string CompartmentCode, DateTimeOffset RequestedAt);
public sealed record UpdateOperationalStatusRequest(string Status, string Reason);
public sealed record CreateMaintenanceRequest(Guid LockerId, Guid? CompartmentId, Guid? IncidentId, MaintenancePriority Priority, string Description);
public sealed record UpdateMaintenanceRequest(MaintenanceStatus Status, string? Notes, string? ResolutionSummary);
public sealed record MaintenanceResponse(Guid Id, string LockerCode, string? CompartmentCode, MaintenancePriority Priority, MaintenanceStatus Status, string Description, string? ResolutionSummary, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record OperationsReportResponse(DateTimeOffset From, DateTimeOffset To, int Deliveries, int Returns, int RetrievedParcels, int OpenIncidents, int MaintenanceRequests, int EmergencyUnlocks);
public sealed record AuditLogResponse(Guid Id, string Actor, string Action, string? EntityType, Guid? EntityId, AuditLogResult Result, string? Details, DateTimeOffset OccurredAt);
public sealed record OverdueTransferResponse(Guid ParcelId, string ParcelCode, string CollectionAddress, string Status);
