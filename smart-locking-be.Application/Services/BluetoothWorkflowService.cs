using System.Globalization;
using System.Security.Cryptography;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Application.Services;

/// <summary>
/// Điều phối D (shipper gửi), P (resident nhận), R (resident gửi), G (khách nhận).
/// N tạo nháp resident gửi tại tủ đang kết nối rồi đi tiếp luồng R.
/// Use case dùng lại kiểm tra chủ sở hữu, session, phê duyệt và phí của service nghiệp vụ;
/// việc trả lệnh Bluetooth chưa có nghĩa hàng đã gửi/nhận thành công.
/// </summary>
public sealed class BluetoothWorkflowService(IBluetoothWorkflowStore store, IBluetoothMessageCodec codec, TimeProvider clock,
    IDeliveryRequestService deliveries, IReturnRequestService returns, IReturnPickupSessionService guests,
    IParcelPickupService parcels, ILockerAccessService access) : IBluetoothWorkflowService
{
    // Kiểm tra chính sách trước mọi luồng; mỗi nhánh kiểm tra đúng quyền trước khi cấp lệnh.
    public async Task<BluetoothCommandResponse> PrepareAsync(Guid? userId, BluetoothPrepareRequest request, CancellationToken ct = default)
    {
        if (request.SubjectId == Guid.Empty) throw new ArgumentException("Cần mã giao dịch hợp lệ.");
        var now = clock.GetUtcNow();
        await RequireEnabledAsync(now, ct);
        if (request.Kind == "N")
        {
            if (string.IsNullOrWhiteSpace(request.DeviceIdentifier)) throw new ArgumentException("Cần mã tủ đang kết nối.");
            var resident = await AvailableResidentAsync(Resident(userId), ct, request.DeviceIdentifier);
            await EnsureReturnDraftAsync(request.SubjectId, resident, now, ct);
            request = request with { Kind = "R" };
        }

        if (!string.IsNullOrWhiteSpace(request.DeviceIdentifier) &&
            await store.GetSubjectDeviceAsync(request.Kind, request.SubjectId, ct) != request.DeviceIdentifier)
            throw new InvalidOperationException("Giao dịch thuộc tủ khác với tủ đang kết nối.");

        BluetoothCommandResponse? command;
        switch (request.Kind)
        {
            // Nhận kiện hàng của chính resident; ParcelPickupService kiểm tra phí và quyền sở hữu.
            case "P":
                command = (await parcels.UnlockAsync(Resident(userId), request.SubjectId, null, "Resident Bluetooth", ct,
                    LockerAccessMethod.Bluetooth)).Bluetooth;
                break;
            // Gửi đồ: Created thì cấp ngăn; Allocated chỉ gửi lại đúng ngăn còn hạn giữ.
            case "R":
                await returns.GetAsync(Resident(userId), request.SubjectId, ct);
                var r = await store.GetReturnAsync(request.SubjectId, ct);
                if (r.Status == ReturnRequestStatus.Created)
                    command = (await returns.OpenCompartmentAsync(userId!.Value, request.SubjectId, ct, LockerAccessMethod.Bluetooth)).Bluetooth;
                else if (r.Status == ReturnRequestStatus.Allocated && r.CompartmentId.HasValue && r.ExpiresAt > now)
                    command = (await access.OpenAsync(new(r.LockerId, r.CompartmentId!.Value, userId, null, null, request.SubjectId,
                        LockerAccessType.ResidentReturnDropOff, LockerAccessMethod.Bluetooth, null, "Resident Bluetooth retry"), ct)).Bluetooth;
                else throw new InvalidOperationException("Yêu cầu không còn chờ gửi đồ.");
                break;
            // Shipper gửi hàng phải có session hợp lệ và yêu cầu đã được duyệt.
            case "D":
                await deliveries.GetAsync(request.SubjectId, Token(request), ct);
                var d = await store.GetDeliveryAsync(request.SubjectId, ct);
                if (d.Status == DeliveryRequestStatus.Approved)
                    command = (await deliveries.OpenCompartmentAsync(request.SubjectId, Token(request), ct, LockerAccessMethod.Bluetooth)).Bluetooth;
                else if (d.Status == DeliveryRequestStatus.Allocated && d.CompartmentId.HasValue && d.ExpiresAt > now)
                    command = (await access.OpenAsync(new(d.LockerId, d.CompartmentId!.Value, null, request.SubjectId, null, null,
                        LockerAccessType.ShipperDropOff, LockerAccessMethod.Bluetooth, null, "Guest Web Bluetooth retry"), ct)).Bluetooth;
                else throw new InvalidOperationException("Yêu cầu chưa được duyệt hoặc không còn chờ gửi hàng.");
                break;
            // Khách nhận đồ resident gửi qua session đã xác minh mã lấy đồ.
            case "G":
                command = (await guests.OpenCompartmentAsync(request.SubjectId, Token(request), ct, LockerAccessMethod.Bluetooth)).Bluetooth;
                break;
            default: throw new ArgumentException("Kind phải là D, P, R, G hoặc N.");
        }
        return command ?? throw new InvalidOperationException("Không thể chuẩn bị lệnh mở ngăn.");
    }

    // Cấp danh tính dùng khi mất mạng trong 24 giờ; chỉ cấp mới khi chính sách Bluetooth đang bật.
    public async Task<ResidentBluetoothPermitResponse> GetPermitAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await RequireEnabledAsync(now, ct);
        var resident = await AvailableResidentAsync(userId, ct);
        var expires = now.AddDays(1);
        string phone = resident.Phone?.Replace("+84", "0", StringComparison.Ordinal) ?? "";
        if (phone.Length != 10 || phone[0] != '0' || !phone.All(char.IsAsciiDigit))
            throw new ArgumentException("Cần số điện thoại cư dân hợp lệ.");
        return new(resident.Device, codec.CreateResidentPermit(resident.Device, userId, phone, expires), expires);
    }

    public async Task AttachImageAsync(Guid userId, Guid returnId, string imageUrl, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || imageUrl.Length > 2048)
            throw new ArgumentException("URL ảnh không hợp lệ.");
        // Bổ sung bằng chứng cho giao dịch đã đồng bộ vẫn được phép khi Bluetooth bị tắt.
        // Chỉ chủ sở hữu được cập nhật ảnh; không phát lệnh mở và không thay đổi trạng thái nhận/gửi.
        var item = await store.FindOwnedReturnAsync(userId, returnId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu của cư dân.");
        item.ReturnImageUrl = imageUrl;
        item.UpdatedAt = clock.GetUtcNow();
        await store.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tạo nháp theo ID client để retry cùng giao dịch, không tạo thêm đơn cho mỗi lần bấm.
    /// Chỉ tái sử dụng nháp thuộc đúng cư dân và tủ đang kết nối. Phân ngăn và mở chốt
    /// vẫn do ReturnRequestService xử lý sau bước này.
    /// </summary>
    private async Task EnsureReturnDraftAsync(Guid id, BluetoothResidentContext resident, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await store.FindReturnDraftAsync(id, ct);
        if (existing is not null)
        {
            if (existing.ResidentProfileId != resident.ProfileId)
                throw new KeyNotFoundException("Không tìm thấy yêu cầu của cư dân.");
            if (existing.LockerId != resident.LockerId)
                throw new InvalidOperationException("Yêu cầu thuộc tủ khác với tủ đang kết nối.");
            return;
        }

        // PIN là mã lấy đồ 6 số, giữ số 0 ở đầu; không dùng ID giao dịch làm PIN.
        string? pin = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string candidate = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            if (!await store.ReturnCodeExistsAsync(candidate, ct))
            {
                pin = candidate;
                break;
            }
        }
        if (pin is null) throw new InvalidOperationException("Chưa cấp được mã lấy đồ. Vui lòng thử lại.");

        store.Add(new ReturnRequest
        {
            Id = id,
            ResidentProfileId = resident.ProfileId,
            LockerId = resident.LockerId,
            ReturnCode = pin,
            ReturnReason = "Bluetooth; image pending upload.",
            Status = ReturnRequestStatus.Created,
            CreatedAt = now,
            UpdatedAt = now
        });
        await store.SaveChangesAsync(ct);
    }

    // Đọc dữ liệu tài khoản/tủ rồi quyết định khả dụng tại Application, không giao quy tắc cho EF store.
    private async Task<BluetoothResidentContext> AvailableResidentAsync(Guid userId, CancellationToken ct, string? deviceIdentifier = null)
    {
        var resident = await store.GetResidentAsync(userId, ct, deviceIdentifier);
        if (resident.AccountStatus != UserStatus.Active || resident.Role != UserRole.Resident ||
            (resident.LockerStatus.HasValue && resident.LockerStatus != LockerOperationalStatus.Operational))
            throw new UnauthorizedAccessException("Cư dân hoặc tủ không khả dụng.");
        return resident;
    }

    private async Task RequireEnabledAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!await store.IsEnabledAsync(now, ct)) throw new InvalidOperationException("Bluetooth đang tắt theo chính sách.");
    }

    private static Guid Resident(Guid? id) => id ?? throw new UnauthorizedAccessException("Cư dân cần đăng nhập.");
    private static string Token(BluetoothPrepareRequest request) => !string.IsNullOrWhiteSpace(request.GuestSessionToken)
        ? request.GuestSessionToken : throw new UnauthorizedAccessException("Khách cần session token.");
}
