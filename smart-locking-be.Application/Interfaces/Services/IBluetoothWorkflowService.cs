using smart_locking_be.Application.DTOs.Lockers;

namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>Điều phối Bluetooth online và cấp dữ liệu chuẩn bị cho trường hợp mất mạng.</summary>
public interface IBluetoothWorkflowService
{
    // Resident dùng userId từ JWT; guest dùng session token trong request. Không tin ID user từ client.
    Task<BluetoothCommandResponse> PrepareAsync(Guid? userId, BluetoothPrepareRequest request, CancellationToken ct = default);
    // Cấp permit cư dân trong thời hạn quy định khi tài khoản và chính sách còn hiệu lực.
    Task<ResidentBluetoothPermitResponse> GetPermitAsync(Guid userId, CancellationToken ct = default);
    // Bổ sung ảnh cho yêu cầu của chính cư dân, không đánh dấu giao dịch đã hoàn tất.
    Task AttachImageAsync(Guid userId, Guid returnId, string imageUrl, CancellationToken ct = default);
}
