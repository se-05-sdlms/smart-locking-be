using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Infrastructure.Services;

public sealed partial class OfflinePinService
{
    // Nhật ký R/G đi chung hàng đợi với D/P, giữ đúng thứ tự của tủ.
    private async Task ApplyReturnAsync(OfflineLockerEvent item, CancellationToken ct)
    {
        // ReceiveAsync owns the encompassing transaction; serialize with cancellation
        // before reloading this request so delayed MQTT cannot restore revoked access.
        await using var transition = await ReturnTransitionLock.AcquireAsync(db, item.DeliveryId, ct);
        var compartment = await FindCompartment(item, ct);
        if (compartment is null) { Review(item, "Unknown return compartment.", null); return; }
        var request = await db.ReturnRequests.Include(x => x.Cancellation).Include(x => x.ResidentProfile).ThenInclude(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == item.DeliveryId, ct);
        var occurred = item.ClosedAt ?? item.ReceivedAt;
        bool notify = request is null || (item.Kind == "R" ? request.Status == ReturnRequestStatus.Allocated : request.Status == ReturnRequestStatus.Deposited);
        if (item.Kind == "R")
        {
            if (request is null)
            {
                // A phone number in unsigned device evidence cannot authorize a new
                // resident obligation or bypass registered-locker change eligibility.
                Review(item, "Offline return requires an existing authorized request; unsigned resident identity requires reconciliation.", compartment, blockCompartment: true);
                return;
            }
            else
            {
                if (request.ReturnCode != item.PinCode || request.LockerId != compartment.LockerId ||
                    request.AllocatedCompartmentId != compartment.Id ||
                    request.ResidentProfile.User.PhoneNumber?.Replace("+84", "0", StringComparison.Ordinal) != item.RecipientPhone ||
                    request.Status is not (ReturnRequestStatus.Allocated or ReturnRequestStatus.Deposited or ReturnRequestStatus.Completed))
                { Review(item, "Return deposit does not match request, code, resident or compartment.", compartment); return; }
                if (request.Status != ReturnRequestStatus.Completed && await CompartmentConflictAsync(compartment.Id, request.Id, occurred, ct))
                { Review(item, "Return compartment is occupied by another transaction.", compartment); return; }
                if (request.Status != ReturnRequestStatus.Completed) request.Status = ReturnRequestStatus.Deposited;
                request.ResidentDepositedAt ??= occurred;
                request.UpdatedAt = item.ReceivedAt;
            }
            foreach (var reservation in await db.CompartmentReservations.Where(x => x.ReturnRequestId == request.Id && x.ReleasedAt == null).ToListAsync(ct))
                reservation.ReleasedAt = occurred;
        }
        else
        {
            if (request is null || request.LockerId != compartment.LockerId || request.AllocatedCompartmentId != compartment.Id ||
                request.ResidentProfile.User.PhoneNumber?.Replace("+84", "0", StringComparison.Ordinal) != item.RecipientPhone ||
                request.Status is not (ReturnRequestStatus.Deposited or ReturnRequestStatus.Completed))
            { Review(item, "Guest pickup does not match a deposited return.", compartment); return; }
            if (await CompartmentConflictAsync(compartment.Id, request.Id, occurred, ct))
            { Review(item, "Guest pickup conflicts with another transaction.", compartment); return; }
            if (request.ResidentDepositedAt > occurred) { Review(item, "Pickup precedes return deposit.", compartment); return; }
            request.Status = ReturnRequestStatus.Completed;
            request.ShipperPickedUpAt ??= occurred;
            request.CompartmentReleasedAt ??= occurred;
            if (request.Cancellation is not null)
            {
                request.Cancellation.Status = "AlreadyCollected";
                request.Cancellation.CompletedAt ??= occurred;
                request.ShipperSessionTokenHash = null;
                db.AuditLogs.Add(new()
                {
                    Id = Guid.NewGuid(),
                    Action = "ReturnCancellation.AlreadyCollected",
                    EntityType = nameof(ReturnRequest),
                    EntityId = request.Id,
                    OccurredAt = item.ReceivedAt,
                    Result = AuditLogResult.Succeeded,
                    Details = "Device pickup completed after cancellation was requested; cancellation did not complete."
                });
            }
            request.UpdatedAt = item.ReceivedAt;
        }
        ObserveClosure(item, compartment);
        // Access này là kết quả cảm biến, không phải chỉ là lệnh mở chốt.
        db.LockerAccessEvents.Add(new()
        {
            Id = item.Id,
            LockerId = compartment.LockerId,
            LockerCompartmentId = compartment.Id,
            ReturnRequestId = request.Id,
            UserId = item.Kind == "R" ? request.ResidentProfile.UserId : null,
            AccessType = item.Kind == "R" ? LockerAccessType.ResidentReturnDropOff : LockerAccessType.ShipperReturnPickup,
            AccessMethod = LockerAccessMethod.Bluetooth,
            Result = LockerAccessResult.Succeeded,
            OccurredAt = item.OpenedAt ?? occurred,
            DeviceContext = "Device door cycle; sequence=" + item.Sequence
        });
        db.AuditLogs.Add(new()
        {
            Id = Guid.NewGuid(),
            Action = "BluetoothReturn." + item.Kind,
            EntityType = nameof(ReturnRequest),
            EntityId = request.Id,
            ActorUserId = item.Kind == "R" ? request.ResidentProfile.UserId : null,
            OccurredAt = item.ReceivedAt,
            Result = AuditLogResult.Succeeded,
            Details = "Door open/close confirmed."
        });
        if (notify)
            foreach (var notificationChannel in new[] { NotificationChannel.InApp, NotificationChannel.Push })
                db.Notifications.Add(new()
                {
                    Id = Guid.NewGuid(),
                    UserId = request.ResidentProfile.UserId,
                    ReturnRequestId = request.Id,
                    Type = item.Kind == "R" ? "ReturnDeposited" : "ReturnPickedUp",
                    Channel = notificationChannel,
                    Title = item.Kind == "R" ? "Da gui do vao tu" : "Guest da lay do",
                    Message = item.Kind == "R"
                        ? request.Cancellation is null
                            ? $"Do da o ngan {compartment.Code}. Mo app de chia se ma lay do."
                            : "Cancellation is pending verification. Do not share the previous pickup code or retrieve the parcel. Contact the locker operator."
                        : $"Do tai ngan {compartment.Code} da duoc lay.",
                    DeliveryStatus = notificationChannel == NotificationChannel.InApp ? NotificationDeliveryStatus.Sent : NotificationDeliveryStatus.Pending,
                    SentAt = notificationChannel == NotificationChannel.InApp ? item.ReceivedAt : null,
                    CreatedAt = item.ReceivedAt
                });
        item.Status = "Applied";
        if (!item.ClosedAt.HasValue || compartment.OperationalStatus != LockerCompartmentOperationalStatus.Operational)
            Review(item, "Physical return imported; event clock or device availability requires review.", compartment);
    }

    private Task<bool> CompartmentConflictAsync(Guid compartment, Guid subject, DateTimeOffset at, CancellationToken ct) =>
        db.LockerCompartments.AnyAsync(c => c.Id == compartment && (
            db.Parcels.Any(p => p.DeliveryRequest.AllocatedCompartmentId == c.Id &&
                (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue)) ||
            db.ReturnRequests.Any(r => r.Id != subject && r.AllocatedCompartmentId == c.Id &&
                (r.Status == ReturnRequestStatus.Allocated || r.Status == ReturnRequestStatus.Deposited)) ||
            db.CompartmentReservations.Any(r => r.LockerCompartmentId == c.Id && r.ReturnRequestId != subject &&
                r.ReleasedAt == null && r.ExpiresAt > at)), ct);
}
