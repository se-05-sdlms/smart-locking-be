using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOfflinePinService
{
    Task<OfflinePinSyncResponse> SyncAsync(OfflinePinSyncRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<OfflinePinEventResponse>> GetEventsAsync(Guid userId, string role, string? device, string? status,
        int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task ConfirmProvisionAsync(string device, string proof, CancellationToken ct = default);
    Task<OfflinePinSyncResult> ReceiveAsync(string device, string proof, CancellationToken ct = default);
}
