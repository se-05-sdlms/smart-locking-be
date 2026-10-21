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
public sealed class CompartmentResetService(ICompartmentResetStore store, ICompartmentResetDispatcher dispatcher, ICompartmentResetProtocol protocol, TimeProvider clock) : ICompartmentResetService
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
        var (_, data, slot) = await ReadAsync(commandId, ct);
        await AuthorizeAsync(userId, role, slot.LockerId, ct);
        if (protocol.ParseResetReceipt(proof).CommandId != commandId) throw new BadRequestException(BluetoothErrorCodes.InvalidMessage, "Phản hồi không khớp yêu cầu.");
        await ReceiveAsync(data.Device, proof, ct);
        return await ResponseAsync(commandId, ct);
    }
    public async Task ReceiveAsync(string device, string proof, CancellationToken ct = default)
    {
        // MQTT và web BLE có thể chuyển cùng phản hồi đồng thời trong một BE.
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
        Parcel? transferred = null;
        if (data.TransferParcelId is { } transferredId)
        {
            transferred = await store.FindTransferParcelAsync(transferredId, ct);
            if (transferred is null || transferred.DeliveryRequestId != data.SubjectId ||
                transferred.DeliveryRequest.AllocatedCompartmentId != slot.Id ||
                transferred.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
                throw new ConflictException(BluetoothErrorCodes.InvalidState, "Kiện hàng chuyển điểm tập kết đã đổi trạng thái; cần đối soát.");
        }
        var now = clock.GetUtcNow();
        var returned = await store.FindReturnAsync(data.SubjectId, ct);
        if (returned is not null)
        {
            // Reset quản trị không được giả thành khách đã nhận. Giữ terminal state cũ nếu đã kết thúc.
            if (returned.Status is ReturnRequestStatus.Allocated or ReturnRequestStatus.Deposited)
            {
                returned.Status = ReturnRequestStatus.Cancelled;
                returned.FailureReason = data.Reason;
            }
            returned.CompartmentReleasedAt ??= now;
            returned.ShipperSessionTokenHash = null;
            returned.UpdatedAt = now;
        }
        var delivery = await store.FindDeliveryAsync(data.SubjectId, ct);
        if (delivery is not null)
        {
            if (delivery.Status == DeliveryRequestStatus.Allocated) delivery.Status = DeliveryRequestStatus.Cancelled;
            delivery.CompartmentReleasedAt ??= now;
            delivery.UpdatedAt = now;
            // Hàng được quản trị lấy ra là Removed, không phải Retrieved của cư dân.
            foreach (var parcel in await store.GetStoredParcelsAsync(delivery.Id, ct))
            {
                store.Add(new ParcelStatusHistory()
                {
                    Id = Guid.NewGuid(),
                    ParcelId = parcel.Id,
                    FromStatus = parcel.Status,
                    ToStatus = ParcelStatus.Removed,
                    ChangedByUserId = audit.ActorUserId,
                    ChangedAt = now,
                    Reason = data.TransferParcelId.HasValue ? "Transferred to overdue collection point" : data.Reason
                });
                parcel.Status = ParcelStatus.Removed;
                parcel.RemovedAt = now;
                parcel.RemovedByUserId = audit.ActorUserId;
                parcel.RemovalReason = data.TransferParcelId.HasValue ? "Transferred to overdue collection point" : data.Reason;
                parcel.UpdatedAt = now;
            }
        }
        foreach (var reservation in await store.GetReservationsAsync(slot.Id, data.SubjectId, ct)) reservation.ReleasedAt = now;
        foreach (var pin in await store.GetPinsAsync(data.SubjectId, ct))
        {
            pin.PinCode = "";
            pin.ConsumedAt = now;
        }
        if (transferred is not null) RecordTransfer(transferred, audit.ActorUserId, now);
        // Khôi phục trạng thái trước yêu cầu, không tự bật lại ngăn vốn đang bảo trì/hỏng.
        slot.OperationalStatus = data.PreviousStatus;
        slot.DoorStatus = receipt.DoorStatus;
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
    }
    public async Task<DTOs.Parcels.OverdueTransferResponse> RequestTransferAsync(Guid userId, string role, Guid parcelId, CancellationToken ct = default)
    {
        var parcel = await store.FindTransferParcelAsync(parcelId, ct)
            ?? throw new NotFoundException(BluetoothErrorCodes.NotFound, "Không tìm thấy kiện hàng.");
        var delivery = parcel.DeliveryRequest;
        await AuthorizeAsync(userId, role, delivery.LockerId, ct);
        if (delivery.AllocatedCompartmentId is not { } slotId || delivery.ResidentProfile is null)
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Kiện hàng không có ngăn hoặc cư dân nhận.");
        // Request này được dùng sau khi Operator đã lấy hàng ra bằng luồng mở vận hành riêng.
        // Retry sau ACK chỉ đọc trạng thái cuối; không phát lệnh hoặc gửi thông báo lần hai.
        if (parcel.Status == ParcelStatus.Removed)
        {
            var applied = (await store.GetAppliedRequestsAsync(slotId, ct))
                .FirstOrDefault(x => JsonSerializer.Deserialize<Details>(x.Details!)?.TransferParcelId == parcel.Id);
            if (applied is not null)
                return new(parcel.Id, parcel.ParcelCode, delivery.Locker.RecoveryAddress, parcel.Status.ToString(), applied.Id, "Applied");
            // Dữ liệu Removed của phiên bản cũ không chứng minh PIN đã được xóa trên ESP.
            throw new ConflictException(BluetoothErrorCodes.InvalidState,
                "Kiện đã được đánh dấu chuyển nhưng chưa có xác nhận thu hồi PIN. Cần đối soát qua chức năng đặt lại ngăn.");
        }
        if (parcel.MaxStorageUntil > clock.GetUtcNow() || parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Kiện hàng chưa đủ điều kiện chuyển điểm tập kết.");
        var result = await RequestCoreAsync(userId, role, delivery.LockerId, slotId,
            new(delivery.Id, true, "Transferred to overdue collection point"), parcel.Id, ct);
        return new(parcel.Id, parcel.ParcelCode, delivery.Locker.RecoveryAddress, parcel.Status.ToString(), result.CommandId, result.Status);
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

    private void RecordTransfer(Parcel parcel, Guid? actorId, DateTimeOffset now)
    {
        var delivery = parcel.DeliveryRequest;
        store.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorId,
            Action = "Parcel.Transferred",
            EntityType = nameof(Parcel),
            EntityId = parcel.Id,
            Result = AuditLogResult.Succeeded,
            Details = delivery.Locker.RecoveryAddress,
            OccurredAt = now
        });
        foreach (var channel in new[] { NotificationChannel.InApp, NotificationChannel.Push })
            store.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = delivery.ResidentProfile!.UserId,
                ParcelId = parcel.Id,
                DeliveryRequestId = delivery.Id,
                Type = "ParcelTransferred",
                Channel = channel,
                Title = "Kiện hàng đã chuyển điểm tập kết",
                Message = $"Nhận kiện tại {delivery.Locker.RecoveryAddress}. PIN cũ đã vô hiệu hóa.",
                DeliveryStatus = channel == NotificationChannel.InApp ? NotificationDeliveryStatus.Sent : NotificationDeliveryStatus.Pending,
                SentAt = channel == NotificationChannel.InApp ? now : null,
                CreatedAt = now
            });
    }

}
