using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>
/// Cổng dữ liệu cho luồng Bluetooth. Trả dữ liệu để Application quyết định quyền và trạng thái;
/// implementation chỉ truy vấn/lưu bằng EF, không tự cấp lệnh mở hoặc quyết định hoàn tất giao dịch.
/// </summary>
public interface IBluetoothWorkflowStore
{
    // Kiểm tra có chính sách Bluetooth đang hiệu lực tại thời điểm UTC được truyền vào.
    Task<bool> IsEnabledAsync(DateTimeOffset now, CancellationToken ct);
    // Lấy dữ liệu resident và tủ để use case kiểm tra quyền trước khi cấp lệnh online.
    Task<BluetoothResidentContext> GetResidentAsync(Guid userId, CancellationToken ct, string? deviceIdentifier = null);
    // D/R/G dùng ID yêu cầu, P dùng ID kiện hàng; trả thiết bị thực sự của giao dịch đó.
    Task<string> GetSubjectDeviceAsync(string kind, Guid id, CancellationToken ct);
    Task<BluetoothReturnContext> GetReturnAsync(Guid id, CancellationToken ct);
    Task<BluetoothDeliveryContext> GetDeliveryAsync(Guid id, CancellationToken ct);
    Task<ReturnRequest?> FindReturnDraftAsync(Guid id, CancellationToken ct);
    Task<bool> ReturnCodeExistsAsync(string code, CancellationToken ct);
    Task<ReturnRequest?> FindOwnedReturnAsync(Guid userId, Guid returnId, CancellationToken ct);
    void Add(ReturnRequest draft);
    // Lưu các thay đổi đã được Application kiểm tra; entity trả về được theo dõi trong cùng scope.
    Task SaveChangesAsync(CancellationToken ct);
}

