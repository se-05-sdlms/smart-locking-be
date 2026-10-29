using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Constants;
using System.Text.Json;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.Services;

/// <summary>Điều phối nghiệp vụ đặt lại ngăn qua các cổng lưu trữ và gửi lệnh.</summary>
public sealed class CompartmentResetService(ICompartmentResetStore store, ICompartmentResetDispatcher dispatcher, ICompartmentResetProtocol protocol, TimeProvider clock,
    IBluetoothCommandSigner? commandSigner = null, IOperationsRealtimeNotifier? realtime = null) : ICompartmentResetService
{
    // Chặn hai adapter áp dụng cùng ACK đồng thời trong một process BE.
    // Transaction/lưu event cùng commandId vẫn là lớp bảo vệ nhất quán dữ liệu.
    private static readonly SemaphoreSlim CompletionLock = new(1, 1);
    // Snapshot nội bộ của yêu cầu audit, không phải DTO trả ra API hoặc entity/bảng mới.
    private sealed record Details(Guid SubjectId, string Device, int Channel, LockerCompartmentOperationalStatus PreviousStatus, string Reason, Guid? TransferParcelId = null);

    private async Task AuthorizeAsync(Guid actor, string role, Guid locker, CancellationToken ct)
    {
        if (role == nameof(UserRole.Administrator)) return;
        if (role == nameof(UserRole.LockerOperator) && await store.IsOperatorAssignedAsync(actor, locker, clock.GetUtcNow(), ct)) return;
        throw new ForbiddenException(BluetoothErrorCodes.AccessDenied, "Bạn không có quyền đặt lại ngăn của tủ này.");
    }
    private async Task<LockerCompartment> SlotAsync(Guid locker, Guid slot, CancellationToken ct) =>
        await store.FindSlotAsync(locker, slot, ct)
        ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy ngăn trong tủ đã chọn.");
    private async Task NoOtherGoodsAsync(Guid slot, Guid subject, CancellationToken ct)
    {
        if (await store.HasOtherGoodsAsync(slot, subject, clock.GetUtcNow(), ct))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Ngăn đang thuộc giao dịch khác. Không được đặt lại bằng mã giao dịch cũ.");
    }
    private Task<AuditLog?> PendingAsync(Guid slot, CancellationToken ct) => store.FindPendingAsync(slot, ct);
    private async Task<(AuditLog Audit, Details Data, LockerCompartment Slot)> ReadAsync(Guid id, CancellationToken ct)
    {
        var audit = await store.FindRequestAsync(id, ct)
            ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy yêu cầu đặt lại ngăn.");
        var data = JsonSerializer.Deserialize<Details>(audit.Details!) ?? throw new ConflictException(BluetoothErrorCodes.InvalidState, "Yêu cầu không hợp lệ.");
        var slot = await store.FindSlotAsync(null, audit.EntityId!.Value, ct)
            ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy ngăn của yêu cầu đặt lại.");
        return (audit, data, slot);
    }
    public async Task<CompartmentResetContext> ContextAsync(Guid userId, string role, Guid lockerId, Guid compartmentId, CancellationToken ct = default)
    {
        await AuthorizeAsync(userId, role, lockerId, ct);
        var slot = await SlotAsync(lockerId, compartmentId, ct);
        var pending = await PendingAsync(slot.Id, ct);
        Guid? subject = pending is not null ? JsonSerializer.Deserialize<Details>(pending.Details!)!.SubjectId :
            await store.FindSuggestedSubjectAsync(slot.Id, ct);
        return new(slot.Locker.DeviceIdentifier, slot.HardwareChannel, subject, pending?.Id);
    }
    // Chỉ khóa cấp ngăn và ghi yêu cầu. Không đánh dấu hàng đã lấy khi chưa có phản hồi ESP.
    public Task<CompartmentResetResponse> RequestAsync(Guid userId, string role, Guid lockerId, Guid compartmentId, CompartmentResetRequest request, CancellationToken ct = default)
        => RequestCoreAsync(userId, role, lockerId, compartmentId, request, null, ct);

    private async Task<CompartmentResetResponse> RequestCoreAsync(Guid userId, string role, Guid lockerId, Guid compartmentId,
        CompartmentResetRequest request, Guid? transferParcelId, CancellationToken ct)
    {
        await AuthorizeAsync(userId, role, lockerId, ct);
        if (!request.ConfirmedEmpty || request.ExpectedSubjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Cần xác nhận đã lấy hết hàng, mã giao dịch và lý do tối đa 500 ký tự.");
        await using var tx = await store.BeginTransactionAsync(ct);
        var slot = await SlotAsync(lockerId, compartmentId, ct);
        if (slot.HardwareChannel is < 1 or > 4) throw new ConflictException(BluetoothErrorCodes.InvalidState, "Firmware chỉ hỗ trợ kênh 1..4.");
        bool known = await store.SubjectBelongsToSlotAsync(request.ExpectedSubjectId, slot.Id, ct);
        if (!known) throw new ConflictException(BluetoothErrorCodes.InvalidState, "Mã giao dịch không thuộc ngăn này.");
        await NoOtherGoodsAsync(slot.Id, request.ExpectedSubjectId, ct);
        var audit = await PendingAsync(slot.Id, ct);
        if (audit is not null && JsonSerializer.Deserialize<Details>(audit.Details!)!.SubjectId != request.ExpectedSubjectId)
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Ngăn đang có yêu cầu đặt lại cho giao dịch khác.");
        // Maintenance có thể chuyển tiếp lại cùng yêu cầu thu hồi qua BLE; không đổi mục đích đã lưu.
        if (audit is not null && transferParcelId.HasValue && JsonSerializer.Deserialize<Details>(audit.Details!)!.TransferParcelId != transferParcelId)
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Ngăn đang có yêu cầu xử lý khác. Hoàn tất yêu cầu đó trước khi chuyển hàng.");
        if (audit is null)
        {
            audit = new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = userId,
                Action = CompartmentResetActions.Requested,
                EntityType = nameof(LockerCompartment),
                EntityId = slot.Id,
                Result = AuditLogResult.Succeeded,
                OccurredAt = clock.GetUtcNow(),
                Details = JsonSerializer.Serialize(new Details(request.ExpectedSubjectId, slot.Locker.DeviceIdentifier, slot.HardwareChannel, slot.OperationalStatus, request.Reason.Trim(), transferParcelId))
            };
            store.Add(audit);
            // Chặn cấp ngăn mới đến khi nhận xác nhận thật từ ESP; không xóa hàng trên BE trước.
            slot.OperationalStatus = LockerCompartmentOperationalStatus.OutOfService;
            slot.UpdatedAt = clock.GetUtcNow();
            await store.SaveChangesAsync(ct);
        }
        // Lưu Pending trước khi gửi ra thiết bị để ACK đến sớm vẫn tìm được yêu cầu.
        if (tx is not null) await tx.CommitAsync(ct);
        var result = await ResponseAsync(audit.Id, ct);
        if (!request.UseBluetooth && result.Command is { } command)
        {
            try { await dispatcher.DispatchResetAsync(result.DeviceIdentifier, result.Channel, command, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Không tự giải phóng ngăn khi mạng lỗi: không biết ESP đã nhận lệnh hay chưa.
                return result with { Command = null, Message = "Chưa gửi được tới tủ. Ngăn vẫn tạm ngừng cấp; thử lại qua MQTT hoặc Bluetooth." };
            }
        }
        if (request.UseBluetooth && result.Command is { } bluetoothCommand)
            return result with
            {
                Command = (commandSigner ?? throw new ConflictException(BluetoothErrorCodes.InvalidState,
                "Chưa cấu hình xác thực lệnh Bluetooth.")).Sign(result.DeviceIdentifier, bluetoothCommand)
            };
        return request.UseBluetooth ? result : result with { Command = null };
    }
    private async Task<CompartmentResetResponse> ResponseAsync(Guid id, CancellationToken ct)
    {
        var (audit, data, slot) = await ReadAsync(id, ct);
        bool applied = await store.IsAppliedAsync(id, ct);
        // Không cấp lại lệnh theo cấu hình cũ: ESP/kênh có thể đã được quản trị thay đổi
        // sau khi yêu cầu Pending được ghi. Kiểm tra trước khi gửi, không đợi đến lúc nhận ACK.
        if (!applied && (slot.Locker.DeviceIdentifier != data.Device || slot.HardwareChannel != data.Channel ||
            slot.OperationalStatus != LockerCompartmentOperationalStatus.OutOfService))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Cấu hình hoặc trạng thái ngăn đã thay đổi. Cần đối soát trước khi gửi lại lệnh.");
        var expires = clock.GetUtcNow().AddMinutes(5);
        var ackIds = await store.GetAcknowledgedEventIdsAsync(data.Device, data.Channel, data.SubjectId, ct);
        string command = protocol.CreateResetCommand(id, data.Device, data.Channel, data.SubjectId, expires, ackIds);
        string? error = await store.GetLastFailureAsync(id, ct);
        return new(id, applied ? "Applied" : "Pending", data.Device, data.Channel, applied ? null : command, expires,
            applied ? slot.DoorStatus == DoorStatus.Closed ? "ESP đã đặt lại ngăn; cửa đã đóng." : "ESP đã đặt lại ngăn; cần đóng cửa." :
            error is null ? "Đang chờ ESP xác nhận. Ngăn tạm ngừng cấp cho giao dịch mới." : error switch
            {
                "FAIL_SYNC_REQUIRED" => "Tủ còn nhật ký chưa đồng bộ. Đồng bộ nhật ký trước rồi thử đặt lại.",
                "FAIL_WRONG_TRANSACTION" => "Giao dịch trên ESP không khớp. Kiểm tra lại đúng ngăn và mã giao dịch.",
                "FAIL_EXPIRED_OR_CLOCK" => "Lệnh hết hạn hoặc tủ chưa có giờ hệ thống. Kiểm tra kết nối tủ rồi thử lại.",
                "FAIL_RELAY_BUSY" => "Chốt đang hoạt động. Chờ ít nhất 2 giây rồi thử lại.",
                "FAIL_STORAGE" => "ESP chưa lưu được trạng thái. Ngăn vẫn chưa được đặt lại.",
                _ => "ESP chưa đặt lại ngăn: " + error
            });
    }
    public async Task<CompartmentResetResponse> GetAsync(Guid userId, string role, Guid commandId, CancellationToken ct = default)
    {
        var (_, _, slot) = await ReadAsync(commandId, ct);
        await AuthorizeAsync(userId, role, slot.LockerId, ct);
        return await ResponseAsync(commandId, ct);
    }
    public async Task<CompartmentResetResponse> ConfirmAsync(Guid userId, string role, Guid commandId, string proof, CancellationToken ct = default)
    {
        var (_, _, slot) = await ReadAsync(commandId, ct);
        await AuthorizeAsync(userId, role, slot.LockerId, ct);
        if (protocol.ParseResetReceipt(proof).CommandId != commandId) throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Phản hồi không khớp yêu cầu.");
        // z2 do HTTP client chuyển tiếp cũng chưa có chữ ký thiết bị. Không dùng nó để
        // giải phóng ngăn hoặc thu hồi PIN; chỉ trả kết quả đã nhận qua adapter MQTT.
        return await ResponseAsync(commandId, ct);
    }
    public async Task ReceiveAsync(string device, string proof, CancellationToken ct = default)
    {
        // Chỉ adapter MQTT được gọi; ACK trùng vẫn chỉ áp dụng một lần.
        await CompletionLock.WaitAsync(ct);
        try { await ApplyReceiptAsync(device, proof, ct); }
        finally { CompletionLock.Release(); }
    }
    private async Task ApplyReceiptAsync(string device, string proof, CancellationToken ct)
    {
        var receipt = protocol.ParseResetReceipt(proof);
        var id = receipt.CommandId;
        await using var tx = await store.BeginTransactionAsync(ct);
        var (audit, data, slot) = await ReadAsync(id, ct);
        if (device != data.Device || receipt.Device != device || receipt.Channel != data.Channel || receipt.SubjectId != data.SubjectId ||
            slot.Locker.DeviceIdentifier != data.Device || slot.HardwareChannel != data.Channel)
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Phản hồi không đúng tủ, ngăn hoặc giao dịch.");
        // MQTT QoS1 và client BLE có thể cùng chuyển một phản hồi; không hủy/giải phóng lần hai.
        if (await store.IsAppliedAsync(id, ct)) return;
        if (receipt.Result != "OK")
        {
            store.Add(new AuditLog()
            {
                Id = Guid.NewGuid(),
                ActorUserId = audit.ActorUserId,
                Action = CompartmentResetActions.Rejected,
                EntityId = id,
                EntityType = "CompartmentReset",
                Result = AuditLogResult.Failed,
                Details = receipt.Result,
                OccurredAt = clock.GetUtcNow()
            });
            await store.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return;
        }
        if (slot.OperationalStatus != LockerCompartmentOperationalStatus.OutOfService)
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Trạng thái vận hành đã đổi khi đang đặt lại; cần đối soát.");
        await NoOtherGoodsAsync(slot.Id, data.SubjectId, ct);
        // A reset receipt confirms credential/state reset only. Physical goods outcomes belong
        // to the explicit resident/Operator workflow, never to a device ACK.
        var now = clock.GetUtcNow();
        foreach (var pin in await store.GetPinsAsync(data.SubjectId, ct))
        {
            pin.PinCode = "";
            pin.ConsumedAt = now;
        }
        // Khôi phục trạng thái trước yêu cầu, không tự bật lại ngăn vốn đang bảo trì/hỏng.
        slot.OperationalStatus = data.PreviousStatus;
        slot.DoorStatus = receipt.DoorStatus;
        // z2 không mang giờ đo của ESP; đây là lúc BE nhận xác nhận cửa, không phải suy từ nút reset.
        slot.DoorStatusObservedAt = now;
        slot.UpdatedAt = now;
        store.Add(new LockerEvent()
        {
            Id = id,
            LockerId = slot.LockerId,
            LockerCompartmentId = slot.Id,
            ActorUserId = audit.ActorUserId,
            EventType = LockerEventType.OperationalStatusChanged,
            PreviousValue = "ResetPending",
            NewValue = "ResetApplied",
            Severity = LockerEventSeverity.Info,
            Reason = data.Reason,
            Details = proof,
            OccurredAt = now,
            ReceivedAt = now
        });
        await store.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        if (realtime is not null) await realtime.PublishToLockerAsync(slot.LockerId,
            new("LockerResetApplied", slot.Id, slot.LockerId, "Applied", null, now), ct);
    }
    public async Task<DTOs.Parcels.OverdueTransferResponse> RequestTransferAsync(Guid userId, string role, Guid parcelId, CancellationToken ct = default)
    {
        var parcel = await store.FindTransferParcelAsync(parcelId, ct)
            ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy kiện hàng.");
        var delivery = parcel.DeliveryRequest;
        await AuthorizeAsync(userId, role, delivery.LockerId, ct);
        if (delivery.AllocatedCompartmentId is not { } slotId || delivery.ResidentProfile is null)
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Kiện hàng không có ngăn hoặc cư dân nhận.");
        var applied = (await store.GetAppliedRequestsAsync(slotId, ct))
            .FirstOrDefault(x => JsonSerializer.Deserialize<Details>(x.Details!)?.TransferParcelId == parcel.Id);
        if (applied is not null)
        {
            // Retry chỉ đọc trạng thái reset. Xác nhận tới điểm tập kết dùng thao tác riêng.
            return new(parcel.Id, parcel.ParcelCode, delivery.Locker.RecoveryAddress, parcel.Status.ToString(), applied.Id, "Applied");
        }
        if (parcel.Status == ParcelStatus.Removed)
            throw new ConflictException(BluetoothErrorCodes.InvalidState,
                "Arrival is recorded but credential revocation is unconfirmed. Reconcile the existing reset before reuse.");
        if (parcel.MaxStorageUntil > clock.GetUtcNow() || parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Kiện hàng chưa đủ điều kiện chuyển điểm tập kết.");
        if (string.IsNullOrWhiteSpace(delivery.Locker.RecoveryAddress))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Locker recovery address is not configured.");
        var result = await RequestCoreAsync(userId, role, delivery.LockerId, slotId,
            new(delivery.Id, true, "Transferred to overdue collection point"), parcel.Id, ct);
        return new(parcel.Id, parcel.ParcelCode, delivery.Locker.RecoveryAddress, parcel.Status.ToString(), result.CommandId, result.Status);
    }

    public async Task<DTOs.Parcels.OverdueTransferResponse> ConfirmTransferArrivalAsync(Guid userId, string role, Guid parcelId,
        DTOs.Parcels.ConfirmRecoveryArrivalRequest request, CancellationToken ct = default)
    {
        if (!request.ConfirmedArrived || request.ResetCommandId == Guid.Empty)
            throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Cần xác nhận đã giao hàng tới điểm tập kết và mã reset hợp lệ.");
        var parcel = await store.FindTransferParcelAsync(parcelId, ct)
            ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy kiện hàng.");
        await AuthorizeAsync(userId, role, parcel.DeliveryRequest.LockerId, ct);
        var (_, data, slot) = await ReadAsync(request.ResetCommandId, ct);
        if (data.TransferParcelId != parcel.Id || data.SubjectId != parcel.DeliveryRequestId ||
            slot.Id != parcel.DeliveryRequest.AllocatedCompartmentId || slot.LockerId != parcel.DeliveryRequest.LockerId ||
            !await store.IsAppliedAsync(request.ResetCommandId, ct))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Chưa có reset được xác nhận cho đúng kiện và ngăn.");
        parcel = await store.ConfirmRecoveryArrivalAsync(userId, parcel.Id, request.ResetCommandId, clock.GetUtcNow(), ct);
        return new(parcel.Id, parcel.ParcelCode, parcel.DeliveryRequest.Locker.RecoveryAddress,
            parcel.Status.ToString(), request.ResetCommandId, "Applied");
    }

    public async Task RetryPendingTransfersAsync(CancellationToken ct = default)
    {
        foreach (var audit in await store.GetPendingRequestsAsync(ct))
        {
            var data = JsonSerializer.Deserialize<Details>(audit.Details!);
            if (data?.TransferParcelId is null) continue;
            try
            {
                var response = await ResponseAsync(audit.Id, ct);
                if (response.Command is { } command)
                    await dispatcher.DispatchResetAsync(response.DeviceIdentifier, response.Channel, command, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Pending được giữ nguyên; worker sẽ thử lại. Chỉ ghi loại lỗi, không ghi PIN/proof.
                store.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = audit.ActorUserId,
                    Action = "Parcel.TransferRetryFailed",
                    EntityType = "CompartmentReset",
                    EntityId = audit.Id,
                    Result = AuditLogResult.Failed,
                    Details = e.GetType().Name,
                    OccurredAt = clock.GetUtcNow()
                });
                await store.SaveChangesAsync(ct);
            }
        }
    }

}
