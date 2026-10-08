using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OperationsService(ApplicationDbContext db, ILockerAccessService lockerAccess, TimeProvider clock, IPushNotificationService pushNotificationService) : IOperationsService
{
    public async Task<PagedResult<OperationalLockerResponse>> GetLockersAsync(Guid userId, string role, CancellationToken ct = default, int pageNumber = 1, int pageSize = 20)
    {
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        DateTimeOffset now = clock.GetUtcNow();
        IQueryable<Locker> query = ScopeLockers(userId, role).AsNoTracking();
        int totalCount = await query.CountAsync(ct);
        List<OperationalLockerResponse> items = await query.OrderBy(x => x.Code)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(x => new OperationalLockerResponse(
            x.Id, x.Code, x.Address, x.OperationalStatus, x.ConnectionStatus, x.LastSeenAt,
            x.Compartments.Count(c => c.OperationalStatus == LockerCompartmentOperationalStatus.Operational && c.DoorStatus == DoorStatus.Closed &&
                !db.Parcels.Any(p => p.DeliveryRequest.AllocatedCompartmentId == c.Id && (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue)) &&
                !db.ReturnRequests.Any(r => r.AllocatedCompartmentId == c.Id && (r.Status == ReturnRequestStatus.Allocated || r.Status == ReturnRequestStatus.Deposited)) &&
                !db.CompartmentReservations.Any(r => r.LockerCompartmentId == c.Id && r.ReleasedAt == null && r.ExpiresAt > now)),
            db.Parcels.Count(p => p.DeliveryRequest.LockerId == x.Id && (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue)),
            x.Incidents.Count(i => i.Status != IncidentStatus.Resolved))).ToListAsync(ct);
        return new PagedResult<OperationalLockerResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PagedResult<OperationalRecordResponse>> SearchAsync(Guid userId, string role, string? query, Guid? lockerId, CancellationToken ct = default, int pageNumber = 1, int pageSize = 20)
    {
        HashSet<Guid> scope = (await ScopeLockers(userId, role).Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        if (lockerId.HasValue && !scope.Contains(lockerId.Value)) throw new UnauthorizedAccessException("Tủ không thuộc phạm vi vận hành.");
        string term = query?.Trim() ?? string.Empty;
        IQueryable<Parcel> parcels = db.Parcels.Include(x => x.DeliveryRequest).ThenInclude(x => x.Locker).Where(x => scope.Contains(x.DeliveryRequest.LockerId));
        IQueryable<ReturnRequest> returns = db.ReturnRequests.Include(x => x.Locker).Where(x => scope.Contains(x.LockerId));
        IQueryable<Incident> incidents = db.Incidents.Include(x => x.Locker).Where(x => x.LockerId.HasValue && scope.Contains(x.LockerId.Value));
        IQueryable<DeliveryRequest> deliveries = db.DeliveryRequests.Include(x => x.Locker).Where(x => scope.Contains(x.LockerId));
        IQueryable<MaintenanceRequest> maintenance = db.MaintenanceRequests.Include(x => x.Locker).Where(x => scope.Contains(x.LockerId));
        IQueryable<LockerEvent> events = db.LockerEvents.Include(x => x.Locker).Where(x => scope.Contains(x.LockerId));
        IQueryable<Locker> lockers = db.Lockers.Where(x => scope.Contains(x.Id));
        IQueryable<LockerCompartment> compartments = db.LockerCompartments.Include(x => x.Locker).Where(x => scope.Contains(x.LockerId));
        if (lockerId.HasValue) { parcels = parcels.Where(x => x.DeliveryRequest.LockerId == lockerId); returns = returns.Where(x => x.LockerId == lockerId); incidents = incidents.Where(x => x.LockerId == lockerId); deliveries = deliveries.Where(x => x.LockerId == lockerId); maintenance = maintenance.Where(x => x.LockerId == lockerId); events = events.Where(x => x.LockerId == lockerId); lockers = lockers.Where(x => x.Id == lockerId); compartments = compartments.Where(x => x.LockerId == lockerId); }
        if (term.Length > 0) { parcels = parcels.Where(x => x.ParcelCode.Contains(term)); returns = returns.Where(x => x.ReturnCode.Contains(term)); incidents = incidents.Where(x => x.Title.Contains(term)); deliveries = deliveries.Where(x => (x.RecipientPhoneSnapshot != null && x.RecipientPhoneSnapshot.Contains(term)) || x.Id.ToString().Contains(term)); maintenance = maintenance.Where(x => x.Description.Contains(term)); events = events.Where(x => (x.Details != null && x.Details.Contains(term)) || (x.Reason != null && x.Reason.Contains(term))); lockers = lockers.Where(x => x.Code.Contains(term) || x.Address.Contains(term)); compartments = compartments.Where(x => x.Code.Contains(term) || x.HardwareCode.Contains(term)); }
        List<OperationalRecordResponse> result = [];
        result.AddRange(await parcels.OrderByDescending(x => x.UpdatedAt).Take(50).Select(x => new OperationalRecordResponse("Parcel", x.Id, x.ParcelCode, x.Status.ToString(), x.DeliveryRequest.Locker.Code, "Kiện hàng cư dân", x.UpdatedAt)).ToListAsync(ct));
        result.AddRange(await returns.OrderByDescending(x => x.UpdatedAt).Take(50).Select(x => new OperationalRecordResponse("Return", x.Id, x.ReturnCode, x.Status.ToString(), x.Locker.Code, "Hàng cư dân gửi", x.UpdatedAt)).ToListAsync(ct));
        result.AddRange(await incidents.OrderByDescending(x => x.UpdatedAt).Take(50).Select(x => new OperationalRecordResponse("Incident", x.Id, x.Id.ToString(), x.Status.ToString(), x.Locker!.Code, x.Title, x.UpdatedAt)).ToListAsync(ct));
        result.AddRange(await deliveries.OrderByDescending(x => x.UpdatedAt).Take(50).Select(x => new OperationalRecordResponse("DeliveryRequest", x.Id, x.Id.ToString(), x.Status.ToString(), x.Locker.Code, x.RecipientPhoneSnapshot ?? "Yêu cầu giao hàng", x.UpdatedAt)).ToListAsync(ct));
        result.AddRange(await maintenance.OrderByDescending(x => x.UpdatedAt).Take(50).Select(x => new OperationalRecordResponse("MaintenanceRequest", x.Id, x.Id.ToString(), x.Status.ToString(), x.Locker.Code, x.Description, x.UpdatedAt)).ToListAsync(ct));
        result.AddRange(await events.OrderByDescending(x => x.OccurredAt).Take(50).Select(x => new OperationalRecordResponse("LockerEvent", x.Id, x.Id.ToString(), x.EventType.ToString(), x.Locker.Code, x.Details ?? x.Reason ?? x.EventType.ToString(), x.OccurredAt)).ToListAsync(ct));
        result.AddRange(await lockers.OrderByDescending(x => x.UpdatedAt).Take(50).Select(x => new OperationalRecordResponse("Locker", x.Id, x.Code, x.OperationalStatus.ToString(), x.Code, x.Address, x.UpdatedAt)).ToListAsync(ct));
        result.AddRange(await compartments.OrderByDescending(x => x.UpdatedAt).Take(50).Select(x => new OperationalRecordResponse("LockerCompartment", x.Id, x.Code, x.OperationalStatus.ToString(), x.Locker.Code, x.HardwareCode, x.UpdatedAt)).ToListAsync(ct));
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        OperationalRecordResponse[] ordered = result.OrderByDescending(x => x.OccurredAt).ToArray();
        return new PagedResult<OperationalRecordResponse>(ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray(), ordered.Length, pageNumber, pageSize);
    }

    public async Task<EmergencyUnlockResponse> EmergencyUnlockAsync(Guid userId, string role, EmergencyUnlockRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Lý do mở khẩn cấp là bắt buộc.");
        await EnsureScope(userId, role, request.LockerId, ct);
        LockerCompartment compartment = await db.LockerCompartments.SingleOrDefaultAsync(x => x.Id == request.CompartmentId && x.LockerId == request.LockerId, ct) ?? throw new KeyNotFoundException("Không tìm thấy ngăn tủ.");
        DateTimeOffset now = clock.GetUtcNow();
        EmergencyUnlock entity = new() { Id = Guid.NewGuid(), OperatorUserId = userId, LockerId = request.LockerId, LockerCompartmentId = compartment.Id, IncidentId = request.IncidentId, Reason = request.Reason.Trim(), Result = EmergencyUnlockResult.Pending, RequestedAt = now };
        db.EmergencyUnlocks.Add(entity);
        OpenLockerResponse opened = await lockerAccess.OpenAsync(new OpenLockerRequest(request.LockerId, compartment.Id, userId, null, null, null, LockerAccessType.OperatorEmergency, LockerAccessMethod.OperatorAuthorization, null, "Operator Web"), ct);
        entity.Result = opened.Result == LockerAccessResult.Succeeded ? EmergencyUnlockResult.Succeeded : EmergencyUnlockResult.Failed;
        entity.CompletedAt = clock.GetUtcNow();
        Audit(userId, "Locker.EmergencyUnlock", nameof(LockerCompartment), compartment.Id, entity.Result.ToString(), now);
        await db.SaveChangesAsync(ct);
        return new(entity.Id, entity.Result, compartment.Code, entity.RequestedAt);
    }

    public async Task<PagedResult<MaintenanceResponse>> GetMaintenanceAsync(Guid userId, string role, CancellationToken ct = default, int pageNumber = 1, int pageSize = 20)
    {
        IQueryable<MaintenanceRequest> q = db.MaintenanceRequests.Include(x => x.Locker).Include(x => x.LockerCompartment);
        if (role == nameof(UserRole.LockerOperator)) q = q.Where(x => db.OperatorAssignments.Any(a => a.OperatorUserId == userId && a.LockerId == x.LockerId && a.RevokedAt == null));
        else if (role != nameof(UserRole.Administrator)) throw new UnauthorizedAccessException();
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await q.CountAsync(ct);
        MaintenanceResponse[] items = (await q.OrderByDescending(x => x.UpdatedAt).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(MapMaintenance).ToArray();
        return new PagedResult<MaintenanceResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<MaintenanceResponse> CreateMaintenanceAsync(Guid userId, string role, CreateMaintenanceRequest request, CancellationToken ct = default)
    {
        await EnsureScope(userId, role, request.LockerId, ct);
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("Mô tả bảo trì là bắt buộc.");
        if (request.CompartmentId.HasValue && !await db.LockerCompartments.AnyAsync(x => x.Id == request.CompartmentId && x.LockerId == request.LockerId, ct)) throw new KeyNotFoundException("Không tìm thấy ngăn tủ.");
        if (request.IncidentId.HasValue && !await db.Incidents.AnyAsync(x => x.Id == request.IncidentId && x.LockerId == request.LockerId && x.LockerCompartmentId == request.CompartmentId, ct)) throw new ArgumentException("Sự cố không thuộc đúng tủ hoặc ngăn được yêu cầu bảo trì.");
        DateTimeOffset now = clock.GetUtcNow();
        MaintenanceRequest entity = new() { Id = Guid.NewGuid(), LockerId = request.LockerId, LockerCompartmentId = request.CompartmentId, CreatedByUserId = userId, IncidentId = request.IncidentId, Priority = request.Priority, Status = MaintenanceStatus.Open, Description = request.Description.Trim(), CreatedAt = now, UpdatedAt = now };
        entity.Activities.Add(new MaintenanceActivity { Id = Guid.NewGuid(), ActionByUserId = userId, ActionType = "Created", ToStatus = MaintenanceStatus.Open, CreatedAt = now });
        db.MaintenanceRequests.Add(entity); Audit(userId, "Maintenance.Created", nameof(MaintenanceRequest), entity.Id, entity.Description, now);
        await db.SaveChangesAsync(ct);
        return await LoadMaintenance(entity.Id, ct);
    }

    public async Task<MaintenanceResponse> UpdateMaintenanceAsync(Guid userId, string role, Guid id, UpdateMaintenanceRequest request, CancellationToken ct = default)
    {
        MaintenanceRequest entity = await db.MaintenanceRequests.Include(x => x.Locker).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu bảo trì.");
        await EnsureScope(userId, role, entity.LockerId, ct);
        if (request.Status == MaintenanceStatus.Closed && string.IsNullOrWhiteSpace(request.ResolutionSummary)) throw new ArgumentException("Cần nhập kết quả bảo trì khi đóng yêu cầu.");
        DateTimeOffset now = clock.GetUtcNow(); MaintenanceStatus previous = entity.Status;
        entity.Status = request.Status; entity.ResolutionSummary = request.ResolutionSummary?.Trim() ?? entity.ResolutionSummary; entity.UpdatedAt = now; entity.ClosedAt = request.Status == MaintenanceStatus.Closed ? now : null;
        db.MaintenanceActivities.Add(new MaintenanceActivity { Id = Guid.NewGuid(), MaintenanceRequestId = id, ActionByUserId = userId, ActionType = "StatusChanged", FromStatus = previous, ToStatus = request.Status, Notes = request.Notes?.Trim(), CreatedAt = now });
        Audit(userId, "Maintenance.StatusChanged", nameof(MaintenanceRequest), id, $"{previous} -> {request.Status}", now);
        await db.SaveChangesAsync(ct); return await LoadMaintenance(id, ct);
    }

    public async Task<OperationsReportResponse> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to <= from) throw new ArgumentException("Khoảng thời gian báo cáo không hợp lệ.");
        return new(from, to,
            await db.DeliveryRequests.CountAsync(x => x.CreatedAt >= from && x.CreatedAt < to, ct),
            await db.ReturnRequests.CountAsync(x => x.CreatedAt >= from && x.CreatedAt < to, ct),
            await db.Parcels.CountAsync(x => x.RetrievedAt >= from && x.RetrievedAt < to, ct),
            await db.Incidents.CountAsync(x => x.Status != IncidentStatus.Resolved && x.CreatedAt < to, ct),
            await db.MaintenanceRequests.CountAsync(x => x.CreatedAt >= from && x.CreatedAt < to, ct),
            await db.EmergencyUnlocks.CountAsync(x => x.RequestedAt >= from && x.RequestedAt < to, ct));
    }

    public async Task<PagedResult<AuditLogResponse>> GetAuditLogsAsync(string? query, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default, int pageNumber = 1, int pageSize = 20)
    {
        IQueryable<AuditLog> q = db.AuditLogs.Include(x => x.ActorUser).AsNoTracking(); string term = query?.Trim() ?? string.Empty;
        if (from.HasValue) q = q.Where(x => x.OccurredAt >= from); if (to.HasValue) q = q.Where(x => x.OccurredAt < to);
        if (term.Length > 0) q = q.Where(x => x.Action.Contains(term) || (x.Details != null && x.Details.Contains(term)));
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int totalCount = await q.CountAsync(ct);
        List<AuditLogResponse> items = await q.OrderByDescending(x => x.OccurredAt).Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(x => new AuditLogResponse(x.Id, x.ActorUser == null ? "System/Guest" : (x.ActorUser.Email ?? x.ActorUser.PhoneNumber ?? x.ActorUser.Id.ToString()), x.Action, x.EntityType, x.EntityId, x.Result, x.Details, x.OccurredAt)).ToListAsync(ct);
        return new PagedResult<AuditLogResponse>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<OverdueTransferResponse> TransferOverdueAsync(Guid userId, string role, Guid parcelId, CancellationToken ct = default)
    {
        Parcel parcel = await db.Parcels.Include(x => x.DeliveryRequest).ThenInclude(x => x.Locker).Include(x => x.DeliveryRequest).ThenInclude(x => x.ResidentProfile).SingleOrDefaultAsync(x => x.Id == parcelId, ct) ?? throw new KeyNotFoundException("Không tìm thấy kiện hàng.");
        ResidentProfile resident = parcel.DeliveryRequest.ResidentProfile ?? throw new InvalidOperationException("Kiện hàng không có cư dân nhận.");
        await EnsureScope(userId, role, parcel.DeliveryRequest.LockerId, ct); DateTimeOffset now = clock.GetUtcNow();
        if (parcel.MaxStorageUntil > now || parcel.Status is not (ParcelStatus.Stored or ParcelStatus.Overdue)) throw new InvalidOperationException("Kiện hàng chưa đủ điều kiện chuyển điểm tập kết.");
        parcel.Status = ParcelStatus.Removed; parcel.RemovedAt = now; parcel.RemovedByUserId = userId; parcel.RemovalReason = "Transferred to overdue collection point"; parcel.UpdatedAt = now;
        string message = $"Nhận kiện tại {parcel.DeliveryRequest.Locker.RecoveryAddress}.";
        db.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = resident.UserId, ParcelId = parcel.Id, DeliveryRequestId = parcel.DeliveryRequestId, Type = "ParcelTransferred", Channel = NotificationChannel.InApp, Title = "Kiện hàng đã chuyển điểm tập kết", Message = message, DeliveryStatus = NotificationDeliveryStatus.Sent, IsRead = false, SentAt = now, CreatedAt = now });
        Notification push = new() { Id = Guid.NewGuid(), UserId = resident.UserId, ParcelId = parcel.Id, DeliveryRequestId = parcel.DeliveryRequestId, Type = "ParcelTransferred", Channel = NotificationChannel.Push, Title = "Kiện hàng đã chuyển điểm tập kết", Message = message, DeliveryStatus = NotificationDeliveryStatus.Pending, IsRead = false, CreatedAt = now };
        db.Notifications.Add(push);
        Audit(userId, "Parcel.Transferred", nameof(Parcel), parcel.Id, parcel.DeliveryRequest.Locker.RecoveryAddress, now); await db.SaveChangesAsync(ct);
        await pushNotificationService.TrySendAsync(push.Id, ct);
        return new(parcel.Id, parcel.ParcelCode, parcel.DeliveryRequest.Locker.RecoveryAddress, parcel.Status.ToString());
    }

    private IQueryable<Locker> ScopeLockers(Guid userId, string role) => role switch { nameof(UserRole.Administrator) => db.Lockers, nameof(UserRole.LockerOperator) => db.Lockers.Where(x => db.OperatorAssignments.Any(a => a.OperatorUserId == userId && a.LockerId == x.Id && a.RevokedAt == null)), _ => throw new UnauthorizedAccessException() };
    private async Task EnsureScope(Guid userId, string role, Guid lockerId, CancellationToken ct) { if (!await ScopeLockers(userId, role).AnyAsync(x => x.Id == lockerId, ct)) throw new UnauthorizedAccessException("Tủ không thuộc phạm vi vận hành."); }
    private void Audit(Guid? userId, string action, string type, Guid id, string? details, DateTimeOffset now) => db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorUserId = userId, Action = action, EntityType = type, EntityId = id, Result = AuditLogResult.Succeeded, Details = details, OccurredAt = now });
    private async Task<MaintenanceResponse> LoadMaintenance(Guid id, CancellationToken ct) => MapMaintenance(await db.MaintenanceRequests.Include(x => x.Locker).Include(x => x.LockerCompartment).SingleAsync(x => x.Id == id, ct));
    private static MaintenanceResponse MapMaintenance(MaintenanceRequest x) => new(x.Id, x.Locker.Code, x.LockerCompartment == null ? null : x.LockerCompartment.Code, x.Priority, x.Status, x.Description, x.ResolutionSummary, x.CreatedAt, x.UpdatedAt);
}
