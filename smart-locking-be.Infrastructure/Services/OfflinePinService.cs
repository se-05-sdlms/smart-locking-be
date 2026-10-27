using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed partial class OfflinePinService(ApplicationDbContext db, IBluetoothMessageCodec protocol, TimeProvider clock, IDeliveryRequestService? deliveries = null) : IOfflinePinService
{
    // JWT xác thực người dùng, không xác thực nguồn ESP. o1 chưa có MAC/chữ ký nên HTTP
    // không được lưu/áp dụng hoặc ACK, kể cả khi người chuyển tiếp đã đăng nhập.
    public Task<OfflinePinSyncResponse> SyncAsync(OfflinePinSyncRequest request, CancellationToken ct = default) =>
        throw new ForbiddenException(BluetoothErrorCodes.UnverifiedEvidence,
            "Unsigned device events cannot be synchronized over HTTP. Let the ESP replay its journal through an authenticated device channel.");

    // Nhật ký chờ/đối soát cũng hiển thị cho quản trị; tuyệt đối không trả PIN hoặc proof chứa PIN.
    public async Task<IReadOnlyList<OfflinePinEventResponse>> GetEventsAsync(Guid userId, string role, string? device, string? status,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (role is not ("Administrator" or "LockerOperator")) throw new ForbiddenException(BluetoothErrorCodes.AccessDenied, "Bạn không có quyền thực hiện thao tác.");
        var query = db.OfflineLockerEvents.AsNoTracking();
        if (role == "LockerOperator")
        {
            DateTimeOffset now = clock.GetUtcNow();
            var assignments = db.EffectiveOperatorAssignments(now);
            query = query.Where(e => db.Lockers.Any(l => l.DeviceIdentifier == e.DeviceIdentifier &&
                assignments.Any(a => a.LockerId == l.Id && a.OperatorUserId == userId && a.RevokedAt == null)));
        }
        if (!string.IsNullOrWhiteSpace(device)) query = query.Where(x => x.DeviceIdentifier == device);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 100);
        return await query.OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Sequence).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OfflinePinEventResponse(x.Id, x.DeliveryId, x.DeviceIdentifier, x.HardwareChannel, x.Kind, x.Sequence,
                x.RecipientPhone, x.OpenedAt, x.ClosedAt, x.ReceivedAt, x.Status, x.ReviewReason)).ToListAsync(ct);
    }

    public async Task ConfirmProvisionAsync(string device, string proof, CancellationToken ct = default)
    {
        var confirmation = protocol.ParsePinProvisioned(proof, device);
        await using IDbContextTransaction? transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        OfflineLockerPin? pin = await db.OfflineLockerPins.SingleOrDefaultAsync(x => x.DeliveryId == confirmation.DeliveryId && x.PinCode == confirmation.Pin &&
            x.ConsumedAt == null && db.Lockers.Any(l => l.Id == x.LockerId && l.DeviceIdentifier == device), ct);
        if (pin is null || pin.ProvisionedAt.HasValue) return;
        Parcel parcel = await db.Parcels.Include(x => x.DeliveryRequest).ThenInclude(x => x.ResidentProfile)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.Locker)
            .Include(x => x.DeliveryRequest).ThenInclude(x => x.AllocatedCompartment).SingleAsync(x => x.Id == pin.ParcelId, ct);
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue)) return;
        pin.ProvisionedAt = clock.GetUtcNow();
        if (parcel.DeliveryRequest.ResidentProfile is { } resident && parcel.DeliveryRequest.AllocatedCompartment is { } compartment)
            OfflinePinProvisioner.AddNotifications(db, parcel, resident.UserId, pin.PinCode, parcel.DeliveryRequest.Locker.Code,
                compartment.Code, pin.ProvisionedAt.Value);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task<OfflinePinSyncResult> ReceiveAsync(string device, string proof, CancellationToken ct = default)
    {
        OfflinePinEvidence evidence = protocol.ParseOfflineEvent(proof, device);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(proof)));
        await using IDbContextTransaction? transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        OfflineLockerEvent? existing = await db.OfflineLockerEvents.SingleOrDefaultAsync(x => x.Id == evidence.EventId, ct);
        if (existing is not null)
        {
            if (existing.DeviceIdentifier != device || existing.PayloadHash != hash) throw new ForbiddenException(BluetoothErrorCodes.AccessDenied, "Conflicting event ID.");
            return Result(existing);
        }
        if (await db.OfflineLockerEvents.AnyAsync(x => x.DeviceIdentifier == device && x.Sequence == evidence.Sequence, ct))
            throw new ForbiddenException(BluetoothErrorCodes.AccessDenied, "Conflicting device sequence. Device storage requires reconciliation.");
        DateTimeOffset now = clock.GetUtcNow();
        OfflineLockerEvent item = new()
        {
            Id = evidence.EventId,
            DeliveryId = evidence.DeliveryId,
            DeviceIdentifier = device,
            Sequence = evidence.Sequence,
            HardwareChannel = evidence.Channel,
            Kind = evidence.Kind,
            RecipientPhone = evidence.Phone,
            PinCode = evidence.Kind is "D" or "R" ? evidence.Pin : null,
            PayloadHash = hash,
            OpenedAt = TrustedTime(evidence.OpenedUnix, now),
            ClosedAt = TrustedTime(evidence.ClosedUnix, now),
            ElapsedMs = evidence.ElapsedMs,
            ReceivedAt = now
        };
        db.OfflineLockerEvents.Add(item);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = "OfflinePin.Received",
            EntityType = nameof(OfflineLockerEvent),
            EntityId = item.Id,
            Result = AuditLogResult.Succeeded,
            OccurredAt = now,
            Details = $"Device={device}; sequence={item.Sequence}; waiting for ordered reconciliation."
        });
        // Lưu trước khi tra theo số thứ tự; transaction chưa commit nên chưa gửi ACK.
        await db.SaveChangesAsync(ct);
        OfflineLockerCursor? cursor = await db.OfflineLockerCursors.SingleOrDefaultAsync(x => x.DeviceIdentifier == device, ct);
        if (cursor is null)
        {
            cursor = new() { DeviceIdentifier = device };
            db.OfflineLockerCursors.Add(cursor);
        }
        while (await db.OfflineLockerEvents.SingleOrDefaultAsync(x => x.DeviceIdentifier == device && x.Sequence == cursor.LastSequence + 1, ct) is { } next)
        {
            if (next.Kind is "R" or "G") await ApplyReturnAsync(next, ct);
            else if (next.Kind == "D") await ApplyDropAsync(next, ct);
            else await ApplyPickupAsync(next, ct);
            next.PinCode = null;
            cursor.LastSequence++;
            await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Result(item);
    }

    private OfflinePinSyncResult Result(OfflineLockerEvent item) =>
        new(item.Id, item.Status, item.ReviewReason, protocol.CreateOfflineAcknowledgment(item.DeviceIdentifier, item.Id));

    private static DateTimeOffset? TrustedTime(long unix, DateTimeOffset now) =>
        unix >= 1704067200 && unix <= now.ToUnixTimeSeconds() + 300 ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;

    // Đơn shipper tạo trên web đã được duyệt; không tạo thêm đơn offline trùng.
    private async Task ApplyApprovedDropAsync(OfflineLockerEvent item, LockerCompartment compartment, CancellationToken ct)
    {
        var delivery = await db.DeliveryRequests.Include(x => x.ResidentProfile).SingleAsync(x => x.Id == item.DeliveryId, ct);
        if (delivery.LockerId != compartment.LockerId || delivery.AllocatedCompartmentId != compartment.Id ||
            delivery.RecipientPhoneSnapshot?.Replace("+84", "0") != item.RecipientPhone ||
            delivery.Status is not (DeliveryRequestStatus.Allocated or DeliveryRequestStatus.Deposited))
        { Review(item, "Drop-off does not match an approved web allocation.", compartment); return; }
        var occurred = item.ClosedAt ?? item.ReceivedAt;
        if (delivery.Status == DeliveryRequestStatus.Allocated)
        {
            if (deliveries is null) throw new ConflictException(BluetoothErrorCodes.InvalidState, "Missing delivery workflow service.");
            if (delivery.ReservationExpiresAt <= occurred)
            { Review(item, "Physical drop-off completed after reservation expiry.", compartment); return; }
            await deliveries.FinalizeDropOffAsync(delivery.Id, occurred, ct);
        }
        var parcel = await db.Parcels.SingleAsync(x => x.DeliveryRequestId == delivery.Id, ct);
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
        { Review(item, "Delivery already moved or retrieved.", compartment); return; }
        var pin = await db.OfflineLockerPins.SingleOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        if (pin is null && delivery.ResidentProfile is { } resident)
            OfflinePinProvisioner.AddPinAndNotifications(db, parcel, resident.UserId, item.RecipientPhone, item.PinCode!,
                compartment.Locker.Code, compartment.Code, item.ReceivedAt);
        else if (pin is not null && (!pin.ProvisionedAt.HasValue || pin.PinCode != item.PinCode))
        {
            // Không đổi mã đã thông báo thành một mã khác trong khi đồng bộ.
            if (pin.ProvisionedAt.HasValue) { Review(item, "Device PIN conflicts with an already provisioned PIN.", compartment); return; }
            pin.PinCode = item.PinCode!; pin.ProvisionedAt = item.ReceivedAt;
            if (delivery.ResidentProfile is { } owner)
                OfflinePinProvisioner.AddNotifications(db, parcel, owner.UserId, pin.PinCode, compartment.Locker.Code, compartment.Code, item.ReceivedAt);
        }
        Access(item, compartment, delivery.Id, parcel.Id, null, LockerAccessType.ShipperDropOff);
        item.Status = "Applied";
        if (!item.ClosedAt.HasValue) Review(item, "Device clock requires review.", compartment);
    }

    private async Task ApplyDropAsync(OfflineLockerEvent item, CancellationToken ct)
    {
        LockerCompartment? compartment = await FindCompartment(item, ct);
        if (compartment is null) { Review(item, "Unknown device or compartment.", null); return; }
        // Shipper dùng web: chỉ nhận đơn đã tạo và được BE cấp ngăn, không tự tạo đơn ngoại tuyến.
        if (!await db.DeliveryRequests.AnyAsync(x => x.Id == item.DeliveryId, ct))
        {
            Review(item, "Shipper drop-off requires an existing approved web request.", compartment);
            return;
        }
        await ApplyApprovedDropAsync(item, compartment, ct);
    }

    private async Task ApplyPickupAsync(OfflineLockerEvent item, CancellationToken ct)
    {
        Guid? parcelId = await db.Parcels.Where(x => x.DeliveryRequestId == item.DeliveryId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        await using var parcelLock = parcelId.HasValue ? await ParcelTransitionLock.AcquireAsync(db, parcelId.Value, ct) : null;
        LockerCompartment? compartment = await FindCompartment(item, ct);
        Parcel? parcel = await db.Parcels.Include(x => x.DeliveryRequest).ThenInclude(x => x.ResidentProfile)
            .SingleOrDefaultAsync(x => x.DeliveryRequestId == item.DeliveryId, ct);
        if (compartment is null || parcel is null || parcel.DeliveryRequest.LockerId != compartment.LockerId ||
            parcel.DeliveryRequest.AllocatedCompartmentId != compartment.Id || (parcel.DeliveryRequest.RecipientPhoneSnapshot?.Replace("+84", "0") != item.RecipientPhone))
        { Review(item, "Pickup does not match an imported delivery and compartment.", compartment); return; }
        if (parcel.Status == ParcelStatus.Retrieved)
        {
            // Trạng thái cửa qua MQTT có thể đã hoàn tất đúng đơn trước khi nhật ký PIN đến.
            await ConsumePinAsync(parcel, compartment, parcel.RetrievedAt ?? item.ReceivedAt, ct);
            Access(item, compartment, item.DeliveryId, parcel.Id, parcel.DeliveryRequest.ResidentProfile?.UserId, LockerAccessType.ResidentPickup);
            item.Status = "Applied";
            return;
        }
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue))
        { Review(item, "Parcel already moved, removed or retrieved; no allocation was changed.", compartment); return; }
        string recoveryParcelId = parcel.Id.ToString();
        if (await db.AuditLogs.AnyAsync(x => x.EntityId == compartment.Id &&
            x.Action == "LockerCompartment.ResetRequested" && x.Details != null && x.Details.Contains(recoveryParcelId), ct))
        { Review(item, "Pickup received after recovery reset was requested; reconcile its physical outcome.", compartment); return; }
        DateTimeOffset occurred = item.ClosedAt ?? item.ReceivedAt;
        if (occurred < parcel.StoredAt) { Review(item, "Pickup precedes deposit; event time requires review.", compartment); return; }
        ParcelStatus before = parcel.Status;
        parcel.Status = ParcelStatus.Retrieved;
        parcel.RetrievedAt = occurred;
        parcel.UpdatedAt = item.ReceivedAt;
        parcel.DeliveryRequest.CompartmentReleasedAt = occurred;
        parcel.DeliveryRequest.UpdatedAt = item.ReceivedAt;
        foreach (CompartmentReservation reservation in await db.CompartmentReservations.Where(x =>
            x.DeliveryRequestId == item.DeliveryId && x.ReleasedAt == null).ToListAsync(ct)) reservation.ReleasedAt = occurred;
        compartment.DoorStatus = DoorStatus.Closed;
        compartment.UpdatedAt = item.ReceivedAt;
        Guid? user = parcel.DeliveryRequest.ResidentProfile?.UserId;
        db.ParcelStatusHistories.Add(new()
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = before,
            ToStatus = ParcelStatus.Retrieved,
            ChangedByUserId = user,
            ChangedAt = occurred,
            Reason = "Offline one-time PIN pickup completed at device."
        });
        Access(item, compartment, item.DeliveryId, parcel.Id, user, LockerAccessType.ResidentPickup);
        item.Status = "Applied";
        if (!item.ClosedAt.HasValue || parcel.PickupDueAt < occurred || await db.OverdueCharges.AnyAsync(x =>
            x.ParcelId == parcel.Id && x.Status == OverdueChargeStatus.Outstanding, ct))
            Review(item, "Physical pickup imported; offline device could not verify central overdue fees or exact time.", compartment);
        await ConsumePinAsync(parcel, compartment, occurred, ct);
    }

    private async Task ConsumePinAsync(Parcel parcel, LockerCompartment compartment, DateTimeOffset occurred, CancellationToken ct)
    {
        OfflineLockerPin? pin = await db.OfflineLockerPins.SingleOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        if (pin is not null) { pin.PinCode = ""; pin.ConsumedAt ??= occurred; }
        foreach (Notification notification in await db.Notifications.Where(x => x.ParcelId == parcel.Id &&
            x.Type == "OfflinePinParcelStored").ToListAsync(ct))
            notification.Message = $"Kiện hàng tại tủ {compartment.Locker.Code}, ngăn {compartment.Code} đã được nhận. PIN cũ đã vô hiệu hóa.";
    }

    private Task<LockerCompartment?> FindCompartment(OfflineLockerEvent item, CancellationToken ct) =>
        db.LockerCompartments.Include(x => x.Locker).SingleOrDefaultAsync(x =>
            x.Locker.DeviceIdentifier == item.DeviceIdentifier && x.HardwareChannel == item.HardwareChannel, ct);

    private void Access(OfflineLockerEvent item, LockerCompartment compartment, Guid delivery, Guid parcel, Guid? user, LockerAccessType type)
    {
        db.LockerAccessEvents.Add(new()
        {
            Id = item.Id,
            LockerId = compartment.LockerId,
            LockerCompartmentId = compartment.Id,
            DeliveryRequestId = delivery,
            ParcelId = parcel,
            UserId = user,
            AccessType = type,
            AccessMethod = LockerAccessMethod.Bluetooth,
            Result = LockerAccessResult.Succeeded,
            DeviceContext = $"Offline PIN; event={item.Id:N}; sequence={item.Sequence}; actor={(type == LockerAccessType.ShipperDropOff ? "guest" : "PIN holder")}",
            OccurredAt = item.OpenedAt ?? item.ReceivedAt
        });
        db.AuditLogs.Add(new()
        {
            Id = Guid.NewGuid(),
            Action = "OfflinePin." + item.Kind,
            EntityType = "OfflineLockerEvent",
            EntityId = item.Id,
            Result = AuditLogResult.Succeeded,
            OccurredAt = item.ReceivedAt,
            Details = "Device event parsed; no PIN in audit log."
        });
    }

    private void Review(OfflineLockerEvent item, string reason, LockerCompartment? compartment)
    {
        item.Status = "NeedsReview";
        item.ReviewReason = reason;
        // Không biến sự kiện không đối chiếu được thành lượt mở thành công. Nếu biết ngăn thật,
        // ghi lượt truy cập lỗi không gắn FK giao dịch chưa được xác minh; nếu đã ghi Success theo
        // thực tế nhận hàng thì giữ kết quả đó và ghi audit đối soát riêng (ví dụ phí chưa thanh toán).
        if (compartment is not null && !db.LockerAccessEvents.Local.Any(x => x.Id == item.Id) &&
            !db.LockerAccessEvents.Any(x => x.Id == item.Id))
            db.LockerAccessEvents.Add(new LockerAccessEvent
            {
                Id = item.Id,
                LockerId = compartment.LockerId,
                LockerCompartmentId = compartment.Id,
                AccessMethod = LockerAccessMethod.Bluetooth,
                AccessType = item.Kind switch
                {
                    "D" => LockerAccessType.ShipperDropOff,
                    "R" => LockerAccessType.ResidentReturnDropOff,
                    "G" => LockerAccessType.ShipperReturnPickup,
                    _ => LockerAccessType.ResidentPickup
                },
                Result = LockerAccessResult.Failed,
                FailureReason = reason,
                DeviceContext = "Offline reconciliation; event=" + item.Id.ToString("N"),
                OccurredAt = item.OpenedAt ?? item.ReceivedAt
            });
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = "OfflinePin.NeedsReview",
            EntityType = nameof(OfflineLockerEvent),
            EntityId = item.Id,
            Result = AuditLogResult.Failed,
            Details = reason,
            OccurredAt = item.ReceivedAt
        });
        db.Incidents.Add(new()
        {
            Id = Guid.NewGuid(),
            Source = IncidentSource.System,
            Status = IncidentStatus.Open,
            Type = "OfflinePinReconciliation",
            Title = "Offline PIN event requires review",
            Description = $"Event {item.Id:N}: {reason}",
            LockerId = compartment?.LockerId,
            LockerCompartmentId = compartment?.Id,
            CreatedAt = item.ReceivedAt,
            UpdatedAt = item.ReceivedAt
        });
    }
}
