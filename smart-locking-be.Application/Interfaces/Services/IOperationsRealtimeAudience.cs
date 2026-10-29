namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>
/// Cổng đọc thiết bị và người nhận cập nhật vận hành. API chỉ dùng kết quả để gửi tin;
/// adapter chịu trách nhiệm truy vấn phạm vi phân công có hiệu lực và tài khoản đang hoạt động.
/// </summary>
public interface IOperationsRealtimeAudience
{
    Task<string?> GetDeviceIdentifierAsync(Guid lockerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Guid>> GetRecipientIdsAsync(Guid lockerId, DateTimeOffset at, CancellationToken cancellationToken = default);
}
