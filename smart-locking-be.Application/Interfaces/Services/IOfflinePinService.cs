using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOfflinePinService
{
    // HTTP relay bị từ chối đến khi giao thức có bằng chứng xác thực nguồn thiết bị.
    Task<OfflinePinSyncResponse> SyncAsync(OfflinePinSyncRequest request, CancellationToken ct = default);
    // Chỉ trả kết quả có sẵn của chính cư dân, không tạo event hoặc hoàn tất giao dịch.
    Task<OfflinePinSyncResponse> ReconcileAsync(Guid userId, OfflinePinReconcileRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<OfflinePinEventResponse>> GetEventsAsync(Guid userId, string role, string? device, string? status,
        int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task ConfirmProvisionAsync(string device, string proof, CancellationToken ct = default);
    // Chỉ adapter MQTT được gọi; chưa có chữ ký thiết bị nên vẫn cần triển khai ACL broker riêng.
    // Device được đối chiếu với topic, không lấy từ HTTP payload làm danh tính đã xác thực.
    Task<OfflinePinSyncResult> ReceiveAsync(string device, string proof, CancellationToken ct = default);
}
