using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOperationsService
{
    Task<IReadOnlyCollection<OperationalLockerResponse>> GetLockersAsync(Guid userId, string role, CancellationToken ct = default);
    Task<IReadOnlyCollection<OperationalRecordResponse>> SearchAsync(Guid userId, string role, string? query, Guid? lockerId, CancellationToken ct = default);
    Task<EmergencyUnlockResponse> EmergencyUnlockAsync(Guid userId, string role, EmergencyUnlockRequest request, CancellationToken ct = default);
    Task<IReadOnlyCollection<MaintenanceResponse>> GetMaintenanceAsync(Guid userId, string role, CancellationToken ct = default);
    Task<MaintenanceResponse> CreateMaintenanceAsync(Guid userId, string role, CreateMaintenanceRequest request, CancellationToken ct = default);
    Task<MaintenanceResponse> UpdateMaintenanceAsync(Guid userId, string role, Guid id, UpdateMaintenanceRequest request, CancellationToken ct = default);
    Task<OperationsReportResponse> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<IReadOnlyCollection<AuditLogResponse>> GetAuditLogsAsync(string? query, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
    Task<OverdueTransferResponse> TransferOverdueAsync(Guid userId, string role, Guid parcelId, CancellationToken ct = default);
}
