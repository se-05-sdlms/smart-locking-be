using smart_locking_be.Application.Constants;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Infrastructure.Persistence;

/// <summary>Adapter EF Core: chỉ truy vấn, theo dõi thay đổi và quản lý transaction.</summary>
public sealed class CompartmentResetStore(ApplicationDbContext db) : ICompartmentResetStore
{
    public Task<bool> IsOperatorAssignedAsync(Guid actorId, Guid lockerId, DateTimeOffset now, CancellationToken ct) =>
        db.EffectiveOperatorAssignments(now).AnyAsync(x => x.OperatorUserId == actorId && x.LockerId == lockerId &&
            x.RevokedAt == null, ct);

    public Task<LockerCompartment?> FindSlotAsync(Guid? lockerId, Guid slotId, CancellationToken ct) =>
        db.LockerCompartments.Include(x => x.Locker)
            .SingleOrDefaultAsync(x => x.Id == slotId && (lockerId == null || x.LockerId == lockerId), ct);

    public async Task<bool> HasOtherGoodsAsync(Guid slotId, Guid subjectId, DateTimeOffset now, CancellationToken ct) =>
        await db.ReturnRequests.AnyAsync(x => x.AllocatedCompartmentId == slotId && x.Id != subjectId &&
            (x.Status == ReturnRequestStatus.Allocated || x.Status == ReturnRequestStatus.Deposited), ct) ||
        await db.DeliveryRequests.AnyAsync(x => x.AllocatedCompartmentId == slotId && x.Id != subjectId &&
            x.Status == DeliveryRequestStatus.Allocated, ct) ||
        await db.Parcels.AnyAsync(x => x.DeliveryRequest.AllocatedCompartmentId == slotId && x.DeliveryRequestId != subjectId &&
            (x.Status == ParcelStatus.Stored || x.Status == ParcelStatus.Overdue), ct) ||
        await db.CompartmentReservations.AnyAsync(x => x.LockerCompartmentId == slotId && x.ReleasedAt == null && x.ExpiresAt > now &&
            x.DeliveryRequestId != subjectId && x.ReturnRequestId != subjectId, ct);

    public Task<AuditLog?> FindPendingAsync(Guid slotId, CancellationToken ct) => db.AuditLogs
        .Where(x => x.EntityId == slotId && x.Action == CompartmentResetActions.Requested && !db.LockerEvents.Any(e => e.Id == x.Id))
        .OrderByDescending(x => x.OccurredAt).FirstOrDefaultAsync(ct);

    public Task<AuditLog?> FindRequestAsync(Guid commandId, CancellationToken ct) =>
        db.AuditLogs.SingleOrDefaultAsync(x => x.Id == commandId && x.Action == CompartmentResetActions.Requested, ct);

    // Ưu tiên giao dịch đang giữ ngăn; nếu đã kết thúc, lấy giao dịch cập nhật gần nhất
    // để hỗ trợ đối soát trường hợp BE hoàn tất nhưng trạng thái ESP còn kẹt.
    public async Task<Guid?> FindSuggestedSubjectAsync(Guid slotId, CancellationToken ct)
    {
        Guid? subject = await db.ReturnRequests.Where(x => x.AllocatedCompartmentId == slotId &&
            (x.Status == ReturnRequestStatus.Allocated || x.Status == ReturnRequestStatus.Deposited))
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        subject ??= await db.DeliveryRequests.Where(x => x.AllocatedCompartmentId == slotId &&
            (x.Status == DeliveryRequestStatus.Allocated || db.Parcels.Any(p => p.DeliveryRequestId == x.Id &&
                (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue))))
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (subject is not null) return subject;

        var returned = await db.ReturnRequests.Where(x => x.AllocatedCompartmentId == slotId).OrderByDescending(x => x.UpdatedAt)
            .Select(x => new { x.Id, x.UpdatedAt }).FirstOrDefaultAsync(ct);
        var delivered = await db.DeliveryRequests.Where(x => x.AllocatedCompartmentId == slotId).OrderByDescending(x => x.UpdatedAt)
            .Select(x => new { x.Id, x.UpdatedAt }).FirstOrDefaultAsync(ct);
        return returned is not null && (delivered is null || returned.UpdatedAt >= delivered.UpdatedAt) ? returned.Id : delivered?.Id;
    }

    public async Task<bool> SubjectBelongsToSlotAsync(Guid subjectId, Guid slotId, CancellationToken ct) =>
        await db.ReturnRequests.AnyAsync(x => x.Id == subjectId && x.AllocatedCompartmentId == slotId, ct) ||
        await db.DeliveryRequests.AnyAsync(x => x.Id == subjectId && x.AllocatedCompartmentId == slotId, ct);

    public Task<bool> IsAppliedAsync(Guid commandId, CancellationToken ct) => db.LockerEvents.AnyAsync(x => x.Id == commandId, ct);

    // Chỉ cho ESP bỏ các nhật ký BE đã lưu (kể cả chờ đối soát), đúng giao dịch/ngăn.
    // Hai event tương ứng lượt gửi và nhận; nhật ký khác vẫn khiến firmware từ chối reset.
    public Task<List<Guid>> GetAcknowledgedEventIdsAsync(string device, int channel, Guid subjectId, CancellationToken ct) =>
        db.OfflineLockerEvents.Where(x => x.DeviceIdentifier == device && x.HardwareChannel == channel &&
            x.DeliveryId == subjectId && (x.Status == "Applied" || x.Status == "NeedsReview"))
            .OrderBy(x => x.Sequence).Select(x => x.Id).Take(2).ToListAsync(ct);

    public Task<string?> GetLastFailureAsync(Guid commandId, CancellationToken ct) => db.AuditLogs
        .Where(x => x.EntityId == commandId && x.Action == CompartmentResetActions.Rejected)
        .OrderByDescending(x => x.OccurredAt).Select(x => x.Details).FirstOrDefaultAsync(ct);

    public Task<ReturnRequest?> FindReturnAsync(Guid subjectId, CancellationToken ct) =>
        db.ReturnRequests.SingleOrDefaultAsync(x => x.Id == subjectId, ct);

    public Task<DeliveryRequest?> FindDeliveryAsync(Guid subjectId, CancellationToken ct) =>
        db.DeliveryRequests.SingleOrDefaultAsync(x => x.Id == subjectId, ct);

    public Task<Parcel?> FindTransferParcelAsync(Guid parcelId, CancellationToken ct) => db.Parcels
        .Include(x => x.DeliveryRequest).ThenInclude(x => x.Locker)
        .Include(x => x.DeliveryRequest).ThenInclude(x => x.ResidentProfile)
        .SingleOrDefaultAsync(x => x.Id == parcelId, ct);

    public async Task<Parcel> ConfirmRecoveryArrivalAsync(Guid actorId, Guid parcelId, Guid resetCommandId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await ParcelTransitionLock.AcquireAsync(db, parcelId, ct);
        Parcel parcel = await db.Parcels.Include(item => item.DeliveryRequest).ThenInclude(item => item.Locker)
            .Include(item => item.DeliveryRequest).ThenInclude(item => item.ResidentProfile)
            .Include(item => item.DeliveryRequest).ThenInclude(item => item.AllocatedCompartment)
            .SingleAsync(item => item.Id == parcelId, ct);
        if (!await db.LockerEvents.AnyAsync(item => item.Id == resetCommandId && item.NewValue == "ResetApplied", ct))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Credential reset is not confirmed.");
        if (parcel.Status == ParcelStatus.Removed && parcel.RemovedAt.HasValue &&
            parcel.RemovalReason == "Transferred to overdue collection point") return parcel;
        if (parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue) || parcel.MaxStorageUntil > now ||
            string.IsNullOrWhiteSpace(parcel.DeliveryRequest.Locker.RecoveryAddress))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "Parcel cannot be confirmed at the recovery address.");
        var slot = parcel.DeliveryRequest.AllocatedCompartment;
        DateTimeOffset? observed = slot?.DoorStatusObservedAt;
        bool closed = slot?.DoorStatus == DoorStatus.Closed;
        if (await db.LockerAccessEvents.AnyAsync(item => item.ParcelId == parcel.Id &&
            item.AccessType == LockerAccessType.ResidentPickup && item.Result == LockerAccessResult.Succeeded &&
            (!closed || !observed.HasValue || item.OccurredAt >= observed.Value), ct))
            throw new ConflictException(BluetoothErrorCodes.InvalidState, "A pickup unlock still awaits reconciliation.");
        ParcelStatus previous = parcel.Status;
        parcel.Status = ParcelStatus.Removed;
        parcel.RemovedAt = now;
        parcel.RemovedByUserId = actorId;
        parcel.RemovalReason = "Transferred to overdue collection point";
        parcel.UpdatedAt = now;
        parcel.DeliveryRequest.CompartmentReleasedAt = now;
        parcel.DeliveryRequest.UpdatedAt = now;
        foreach (var reservation in await db.CompartmentReservations.Where(item => item.DeliveryRequestId == parcel.DeliveryRequestId && item.ReleasedAt == null).ToListAsync(ct))
            reservation.ReleasedAt = now;
        db.ParcelStatusHistories.Add(new ParcelStatusHistory
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            FromStatus = previous,
            ToStatus = ParcelStatus.Removed,
            Reason = "Operator confirmed delivery to the locker recovery address.",
            ChangedByUserId = actorId,
            ChangedAt = now
        });
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorId,
            Action = "Parcel.Transferred",
            EntityType = nameof(Parcel),
            EntityId = parcel.Id,
            Result = AuditLogResult.Succeeded,
            Details = parcel.DeliveryRequest.Locker.RecoveryAddress,
            OccurredAt = now
        });
        foreach (var channel in new[] { NotificationChannel.InApp, NotificationChannel.Push })
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = parcel.DeliveryRequest.ResidentProfile!.UserId,
                ParcelId = parcel.Id,
                DeliveryRequestId = parcel.DeliveryRequestId,
                Type = "ParcelTransferred",
                Channel = channel,
                Title = "Parcel transferred to collection point",
                Message = $"Collect your parcel at {parcel.DeliveryRequest.Locker.RecoveryAddress}.",
                DeliveryStatus = channel == NotificationChannel.InApp ? NotificationDeliveryStatus.Sent : NotificationDeliveryStatus.Pending,
                SentAt = channel == NotificationChannel.InApp ? now : null,
                CreatedAt = now
            });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return parcel;
    }

    public Task<List<AuditLog>> GetPendingRequestsAsync(CancellationToken ct) => db.AuditLogs
        .Where(x => x.Action == CompartmentResetActions.Requested && !db.LockerEvents.Any(e => e.Id == x.Id))
        .OrderBy(x => x.OccurredAt).ToListAsync(ct);

    public Task<List<AuditLog>> GetAppliedRequestsAsync(Guid slotId, CancellationToken ct) => db.AuditLogs
        .Where(x => x.EntityId == slotId && x.Action == CompartmentResetActions.Requested && db.LockerEvents.Any(e => e.Id == x.Id))
        .OrderByDescending(x => x.OccurredAt).ToListAsync(ct);

    public Task<List<Parcel>> GetStoredParcelsAsync(Guid deliveryId, CancellationToken ct) => db.Parcels
        .Where(x => x.DeliveryRequestId == deliveryId && (x.Status == ParcelStatus.Stored || x.Status == ParcelStatus.Overdue)).ToListAsync(ct);

    public Task<List<CompartmentReservation>> GetReservationsAsync(Guid slotId, Guid subjectId, CancellationToken ct) =>
        db.CompartmentReservations.Where(x => x.LockerCompartmentId == slotId && x.ReleasedAt == null &&
            (x.DeliveryRequestId == subjectId || x.ReturnRequestId == subjectId)).ToListAsync(ct);

    public Task<List<OfflineLockerPin>> GetPinsAsync(Guid subjectId, CancellationToken ct) =>
        db.OfflineLockerPins.Where(x => x.DeliveryId == subjectId && x.ConsumedAt == null).ToListAsync(ct);

    public void Add(AuditLog audit) => db.AuditLogs.Add(audit);
    public void Add(ParcelStatusHistory history) => db.ParcelStatusHistories.Add(history);
    public void Add(LockerEvent lockerEvent) => db.LockerEvents.Add(lockerEvent);
    public void Add(Notification notification) => db.Notifications.Add(notification);
    public async Task SaveChangesAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);

    // Serializable giữ nhất quán giữa kiểm tra giao dịch khác, đặt OutOfService và ghi nhận ACK.
    // Provider InMemory dùng trong kiểm tra không hỗ trợ transaction quan hệ.
    public async Task<ICompartmentResetTransaction?> BeginTransactionAsync(CancellationToken ct) => db.Database.IsRelational()
        ? new Transaction(await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)) : null;

    private sealed class Transaction(IDbContextTransaction transaction) : ICompartmentResetTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
