using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Infrastructure.Persistence;

// Chỉ truy vấn và lưu dữ liệu; không quyết định luồng MQTT hay Bluetooth.
public sealed class BluetoothWorkflowStore(ApplicationDbContext db) : IBluetoothWorkflowStore
{
    // Query theo cùng thời điểm UTC của use case để tránh mỗi nhánh tự lấy giờ khác nhau.
    public Task<bool> IsEnabledAsync(DateTimeOffset now, CancellationToken ct) =>
        db.SystemPolicies.AnyAsync(x => x.IsActive && x.EnableBluetoothUnlock && x.EffectiveFrom <= now &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo > now), ct);

    public async Task<BluetoothResidentContext> GetResidentAsync(Guid userId, CancellationToken ct, string? deviceIdentifier = null)
    {
        var resident = await db.ResidentProfiles.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy resident.");
        var locker = string.IsNullOrWhiteSpace(deviceIdentifier) ? null :
            await db.Lockers.SingleOrDefaultAsync(x => x.DeviceIdentifier == deviceIdentifier, ct);
        if (!string.IsNullOrWhiteSpace(deviceIdentifier) && locker is null) throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy tủ đang kết nối.");
        // Trả dữ liệu gốc; Application quyết định tài khoản/tủ có được thao tác hay không.
        // "*" biểu diễn truy vấn không gắn tủ; không phải mã thiết bị hay quyền mở offline.
        return new(resident.Id, locker?.Id ?? Guid.Empty, locker?.DeviceIdentifier ?? "*", resident.User.PhoneNumber,
            resident.User.Status, resident.User.Role, locker?.OperationalStatus);
    }

    // Kiểm tra tủ của giao dịch trước khi cấp lệnh; không dựa vào tủ đăng ký của resident.
    public async Task<string> GetSubjectDeviceAsync(string kind, Guid id, CancellationToken ct)
    {
        string? device = kind switch
        {
            "R" or "G" => await db.ReturnRequests.Where(x => x.Id == id).Select(x => x.Locker.DeviceIdentifier).SingleOrDefaultAsync(ct),
            "P" => await db.Parcels.Where(x => x.Id == id).Select(x => x.DeliveryRequest.Locker.DeviceIdentifier).SingleOrDefaultAsync(ct),
            "D" => await db.DeliveryRequests.Where(x => x.Id == id).Select(x => x.Locker.DeviceIdentifier).SingleOrDefaultAsync(ct),
            _ => throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Loại giao dịch không hợp lệ.")
        };
        return device ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy giao dịch.");
    }

    // Chỉ projection dữ liệu cần cho retry; quyền sở hữu/session đã được use case kiểm tra qua service gốc.
    public async Task<BluetoothReturnContext> GetReturnAsync(Guid id, CancellationToken ct) =>
        await db.ReturnRequests.Where(x => x.Id == id).Select(x => new BluetoothReturnContext(x.LockerId,
            x.AllocatedCompartmentId, x.Status, x.ReservationExpiresAt)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy yêu cầu gửi đồ.");

    public async Task<BluetoothDeliveryContext> GetDeliveryAsync(Guid id, CancellationToken ct) =>
        await db.DeliveryRequests.Where(x => x.Id == id).Select(x => new BluetoothDeliveryContext(x.LockerId,
            x.AllocatedCompartmentId, x.Status, x.ReservationExpiresAt)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy yêu cầu giao hàng.");

    public Task<ReturnRequest?> FindReturnDraftAsync(Guid id, CancellationToken ct) =>
        db.ReturnRequests.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> ReturnCodeExistsAsync(string code, CancellationToken ct) =>
        db.ReturnRequests.AnyAsync(x => x.ReturnCode == code, ct);

    // Lọc chủ sở hữu ngay trong truy vấn để không trả entity thuộc cư dân khác cho thao tác gắn ảnh.
    public Task<ReturnRequest?> FindOwnedReturnAsync(Guid userId, Guid returnId, CancellationToken ct) =>
        db.ReturnRequests.SingleOrDefaultAsync(x => x.Id == returnId && x.ResidentProfile.UserId == userId, ct);

    public void Add(ReturnRequest draft) => db.ReturnRequests.Add(draft);

    public async Task SaveChangesAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
