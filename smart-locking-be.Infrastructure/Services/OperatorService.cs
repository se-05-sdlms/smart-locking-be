using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operator;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OperatorService(ApplicationDbContext dbContext) : IOperatorService
{
    public async Task<OperatorDashboardResponse> GetDashboardAsync(
        Guid operatorUserId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid[] lockerIds = await GetAssignedLockerIdsAsync(operatorUserId, cancellationToken);
        var lockers = await dbContext.Lockers.AsNoTracking()
            .Where(l => lockerIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Code, l.Address, l.OperationalStatus, l.ConnectionStatus })
            .ToListAsync(cancellationToken);
        var compartments = await dbContext.LockerCompartments.AsNoTracking()
            .Where(c => lockerIds.Contains(c.LockerId))
            .Select(c => new { c.Id, c.LockerId, c.OperationalStatus })
            .ToListAsync(cancellationToken);
        List<ParcelRow> activeParcels = await ProjectParcels(ActiveParcels(lockerIds))
            .ToListAsync(cancellationToken);
        var depositedReturns = await dbContext.ReturnRequests.AsNoTracking()
            .Where(r => lockerIds.Contains(r.LockerId) && r.Status == ReturnRequestStatus.Deposited && r.AllocatedCompartmentId != null)
            .Select(r => r.AllocatedCompartmentId!.Value)
            .ToListAsync(cancellationToken);
        var reservations = await dbContext.CompartmentReservations.AsNoTracking()
            .Where(r => r.ReleasedAt == null && r.ExpiresAt > now && lockerIds.Contains(r.LockerCompartment.LockerId))
            .Select(r => r.LockerCompartmentId)
            .ToListAsync(cancellationToken);
        var incidents = await ScopedIncidents(lockerIds)
            .Select(i => new { i.LockerId, i.Status })
            .ToListAsync(cancellationToken);

        List<ParcelRow> overdue = activeParcels
            .Where(p => p.PickupDueAt <= now)
            .OrderBy(p => p.PickupDueAt)
            .ToList();
        HashSet<Guid> occupiedCompartments = activeParcels.Where(p => p.CompartmentId != null)
            .Select(p => p.CompartmentId!.Value).Concat(depositedReturns).ToHashSet();
        HashSet<Guid> overdueCompartments = overdue.Where(p => p.CompartmentId != null)
            .Select(p => p.CompartmentId!.Value).ToHashSet();
        HashSet<Guid> reservedCompartments = reservations.ToHashSet();
        int unavailable = compartments.Count(c => c.OperationalStatus != LockerCompartmentOperationalStatus.Operational);
        int overdueCount = compartments.Count(c => c.OperationalStatus == LockerCompartmentOperationalStatus.Operational && overdueCompartments.Contains(c.Id));
        int occupied = compartments.Count(c => c.OperationalStatus == LockerCompartmentOperationalStatus.Operational && occupiedCompartments.Contains(c.Id) && !overdueCompartments.Contains(c.Id));
        int reserved = compartments.Count(c => c.OperationalStatus == LockerCompartmentOperationalStatus.Operational && !occupiedCompartments.Contains(c.Id) && reservedCompartments.Contains(c.Id));
        int available = compartments.Count - unavailable - overdueCount - occupied - reserved;

        var overdueGroups = overdue.GroupBy(p => p.LockerId)
            .ToDictionary(g => g.Key, g => g.Count());
        var openIncidentGroups = incidents
            .Where(i => i.Status is IncidentStatus.Open or IncidentStatus.Investigating)
            .GroupBy(i => i.LockerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        var overdueByLocker = lockers.Where(l => overdueGroups.ContainsKey(l.Id))
            .Select(l => new OverdueByLockerResponse(l.Id, l.Code, overdueGroups[l.Id]))
            .OrderByDescending(x => x.Count).ThenBy(x => x.LockerCode).ToList();
        var attentionLockers = lockers
            .Where(l => l.OperationalStatus != LockerOperationalStatus.Operational ||
                        l.ConnectionStatus != LockerConnectionStatus.Online ||
                        overdueGroups.ContainsKey(l.Id) || openIncidentGroups.ContainsKey(l.Id))
            .Select(l => new LockerAttentionResponse(
                l.Id, l.Code, l.Address, l.OperationalStatus, l.ConnectionStatus,
                compartments.Count(c => c.LockerId == l.Id),
                overdueGroups.GetValueOrDefault(l.Id), openIncidentGroups.GetValueOrDefault(l.Id)))
            .OrderByDescending(l => l.OpenIncidents)
            .ThenByDescending(l => l.OverdueParcels)
            .ThenBy(l => l.LockerCode)
            .Take(5).ToList();

        return new OperatorDashboardResponse(
            now,
            new LockerStatusSummaryResponse(
                lockers.Count,
                lockers.Count(l => l.OperationalStatus == LockerOperationalStatus.Operational),
                lockers.Count(l => l.OperationalStatus == LockerOperationalStatus.OutOfService),
                lockers.Count(l => l.OperationalStatus == LockerOperationalStatus.Inactive),
                lockers.Count(l => l.ConnectionStatus == LockerConnectionStatus.Online),
                lockers.Count(l => l.ConnectionStatus == LockerConnectionStatus.Offline)),
            new CompartmentStatusSummaryResponse(compartments.Count, available, occupied, overdueCount, reserved, unavailable),
            new ParcelSummaryResponse(activeParcels.Count, overdue.Count),
            new IncidentSummaryResponse(
                incidents.Count(i => i.Status == IncidentStatus.Open),
                incidents.Count(i => i.Status == IncidentStatus.Investigating),
                incidents.Count(i => i.Status == IncidentStatus.Escalated),
                incidents.Count(i => i.Status == IncidentStatus.Resolved),
                incidents.Count(i => i.Status is IncidentStatus.Open or IncidentStatus.Investigating)),
            overdueByLocker,
            attentionLockers,
            overdue.Take(5).Select(p => MapParcel(p, now)).ToList(),
            await GetTodayActivitiesAsync(operatorUserId, lockerIds, now, cancellationToken));
    }

    public async Task<PagedResult<OperatorIncidentResponse>> GetIncidentsAsync(
        Guid operatorUserId, Guid? lockerId = null, IncidentStatus? status = null,
        int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        ValidatePaging(pageNumber, pageSize);
        if (status.HasValue && !Enum.IsDefined(status.Value))
            throw new ArgumentException("Trạng thái sự cố không hợp lệ.", nameof(status));

        Guid[] lockerIds = await GetAssignedLockerIdsAsync(operatorUserId, cancellationToken);
        IQueryable<ScopedIncident> query = ScopedIncidents(lockerIds);
        if (lockerId.HasValue) query = query.Where(i => i.LockerId == lockerId);
        if (status.HasValue) query = query.Where(i => i.Status == status);

        int total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = rows.Select(i => new OperatorIncidentResponse(
            i.Id, i.LockerId!.Value, i.LockerCode!, i.CompartmentId, i.CompartmentCode,
            i.Type, i.Source, i.Status, i.Title, i.Description,
            i.AssignedOperatorUserId, i.CreatedAt, i.UpdatedAt)).ToList();
        return new PagedResult<OperatorIncidentResponse>(items, total, pageNumber, pageSize);
    }

    public async Task<PagedResult<OverdueParcelResponse>> GetOverdueParcelsAsync(
        Guid operatorUserId, Guid? lockerId = null, string? search = null,
        bool? threeDaysOrMore = null, int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        ValidatePaging(pageNumber, pageSize);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid[] lockerIds = await GetAssignedLockerIdsAsync(operatorUserId, cancellationToken);
        IQueryable<Parcel> query = OverdueParcels(lockerIds, now);
        if (lockerId.HasValue) query = query.Where(p => p.DeliveryRequest.LockerId == lockerId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLower();
            query = query.Where(p => p.ParcelCode.ToLower().Contains(term));
        }
        if (threeDaysOrMore.HasValue)
        {
            DateTimeOffset threshold = now.AddDays(-3);
            query = threeDaysOrMore.Value
                ? query.Where(p => p.PickupDueAt <= threshold)
                : query.Where(p => p.PickupDueAt > threshold);
        }

        int total = await query.CountAsync(cancellationToken);
        List<ParcelRow> rows = await ProjectParcels(query.OrderBy(p => p.PickupDueAt).ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<OverdueParcelResponse>(
            rows.Select(p => MapParcel(p, now)).ToList(), total, pageNumber, pageSize);
    }

    public async Task<OverdueParcelResponse> GetOverdueParcelAsync(
        Guid operatorUserId, Guid parcelId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid[] lockerIds = await GetAssignedLockerIdsAsync(operatorUserId, cancellationToken);
        ParcelRow? row = await ProjectParcels(OverdueParcels(lockerIds, now).Where(p => p.Id == parcelId))
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? throw new KeyNotFoundException("Không tìm thấy bưu kiện quá hạn trong phạm vi được phân công.")
            : MapParcel(row, now);
    }

    private async Task<Guid[]> GetAssignedLockerIdsAsync(Guid operatorUserId, CancellationToken cancellationToken) =>
        await dbContext.OperatorAssignments.AsNoTracking()
            .Where(a => a.OperatorUserId == operatorUserId && a.RevokedAt == null).Select(a => a.LockerId)
            .ToArrayAsync(cancellationToken);

    private IQueryable<ScopedIncident> ScopedIncidents(Guid[] lockerIds) => dbContext.Incidents.AsNoTracking()
        .Select(i => new ScopedIncident
        {
            Id = i.Id,
            LockerId = i.LockerId
                ?? (i.LockerCompartment != null ? (Guid?)i.LockerCompartment.LockerId : null)
                ?? (i.DeliveryRequest != null ? (Guid?)i.DeliveryRequest.LockerId : null)
                ?? (i.Parcel != null ? (Guid?)i.Parcel.DeliveryRequest.LockerId : null),
            LockerCode = i.Locker != null ? i.Locker.Code
                : i.LockerCompartment != null ? i.LockerCompartment.Locker.Code
                : i.DeliveryRequest != null ? i.DeliveryRequest.Locker.Code
                : i.Parcel != null ? i.Parcel.DeliveryRequest.Locker.Code : null,
            CompartmentId = i.LockerCompartmentId,
            CompartmentCode = i.LockerCompartment != null ? i.LockerCompartment.Code : null,
            Type = i.Type,
            Source = i.Source,
            Status = i.Status,
            Title = i.Title,
            Description = i.Description,
            AssignedOperatorUserId = i.AssignedOperatorUserId,
            CreatedAt = i.CreatedAt,
            UpdatedAt = i.UpdatedAt
        })
        .Where(i => i.LockerId != null && lockerIds.Contains(i.LockerId.Value));

    private IQueryable<Parcel> ActiveParcels(Guid[] lockerIds) => dbContext.Parcels.AsNoTracking()
        .Where(p => lockerIds.Contains(p.DeliveryRequest.LockerId) &&
                    (p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue) &&
                    p.RetrievedAt == null && p.RemovedAt == null);

    private IQueryable<Parcel> OverdueParcels(Guid[] lockerIds, DateTimeOffset now) =>
        ActiveParcels(lockerIds).Where(p => p.PickupDueAt <= now);

    private static IQueryable<ParcelRow> ProjectParcels(IQueryable<Parcel> query) => query.Select(p => new ParcelRow(
        p.Id, p.ParcelCode,
        p.DeliveryRequest.LockerId, p.DeliveryRequest.Locker.Code, p.DeliveryRequest.Locker.Address,
        p.DeliveryRequest.AllocatedCompartmentId,
        p.DeliveryRequest.AllocatedCompartment != null ? p.DeliveryRequest.AllocatedCompartment.Code : null,
        p.StoredAt, p.PickupDueAt, p.MaxStorageUntil,
        p.DeliveryRequest.ResidentProfile != null ? p.DeliveryRequest.ResidentProfile.FullName : null,
        p.DeliveryRequest.ResidentProfile != null ? p.DeliveryRequest.ResidentProfile.User.PhoneNumber : null,
        p.DeliveryRequest.Locker.RecoveryAddress));

    private static OverdueParcelResponse MapParcel(ParcelRow row, DateTimeOffset now) => new(
        row.Id, row.ParcelCode, row.LockerId, row.LockerCode, row.LockerAddress,
        row.CompartmentId, row.CompartmentCode, row.StoredAt, row.PickupDueAt, row.MaxStorageUntil,
        now - row.PickupDueAt, row.PickupDueAt <= now.AddDays(-3),
        row.ResidentName, MaskPhone(row.ResidentPhone), row.RecoveryAddress,
        now >= row.MaxStorageUntil);

    private static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        return phone.Length <= 6 ? "***" : $"{phone[..3]} *** {phone[^4..]}";
    }

    private async Task<IReadOnlyList<OperatorActivityResponse>> GetTodayActivitiesAsync(
        Guid operatorUserId, Guid[] lockerIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset localNow = now.ToOffset(TimeSpan.FromHours(7));
        DateTimeOffset start = new(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, TimeSpan.FromHours(7));
        var access = await dbContext.LockerAccessEvents.AsNoTracking()
            .Where(e => lockerIds.Contains(e.LockerId) && e.OccurredAt >= start && e.OccurredAt <= now)
            .OrderByDescending(e => e.OccurredAt).Take(5)
            .Select(e => new { e.Id, e.LockerId, LockerCode = e.Locker.Code, e.AccessType, e.Result, e.OccurredAt })
            .ToListAsync(cancellationToken);
        var events = await dbContext.LockerEvents.AsNoTracking()
            .Where(e => lockerIds.Contains(e.LockerId) && e.OccurredAt >= start && e.OccurredAt <= now)
            .OrderByDescending(e => e.OccurredAt).Take(5)
            .Select(e => new { e.Id, e.LockerId, LockerCode = e.Locker.Code, e.EventType, e.Reason, e.OccurredAt })
            .ToListAsync(cancellationToken);
        var parcelChanges = await dbContext.ParcelStatusHistories.AsNoTracking()
            .Where(e => lockerIds.Contains(e.Parcel.DeliveryRequest.LockerId) && e.ChangedAt >= start && e.ChangedAt <= now)
            .OrderByDescending(e => e.ChangedAt).Take(5)
            .Select(e => new
            {
                e.Id,
                LockerId = e.Parcel.DeliveryRequest.LockerId,
                LockerCode = e.Parcel.DeliveryRequest.Locker.Code,
                e.Parcel.ParcelCode,
                e.ToStatus,
                e.ChangedAt
            })
            .ToListAsync(cancellationToken);
        var lastLogin = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == operatorUserId)
            .Select(u => u.LastLoginAt)
            .FirstOrDefaultAsync(cancellationToken);
        IEnumerable<OperatorActivityResponse> activities = access.Select(e => new OperatorActivityResponse(
                e.Id, e.LockerId, e.LockerCode, "LockerAccess",
                $"{e.AccessType}: {e.Result}", e.OccurredAt))
            .Concat(events.Select(e => new OperatorActivityResponse(
                e.Id, e.LockerId, e.LockerCode, "LockerEvent",
                e.Reason ?? e.EventType.ToString(), e.OccurredAt)))
            .Concat(parcelChanges.Select(e => new OperatorActivityResponse(
                e.Id, e.LockerId, e.LockerCode, "ParcelStatus",
                $"{e.ParcelCode}: {e.ToStatus}", e.ChangedAt)));
        if (lastLogin >= start && lastLogin <= now)
            activities = activities.Append(new OperatorActivityResponse(
                operatorUserId, null, null, "Login", "Đăng nhập hệ thống", lastLogin.Value));
        return activities.OrderByDescending(e => e.OccurredAt).Take(5).ToList();
    }

    private static void ValidatePaging(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100 || pageNumber > int.MaxValue / pageSize)
            throw new ArgumentException("pageNumber phải >= 1 và pageSize phải trong khoảng 1-100.");
    }

    private sealed record ParcelRow(
        Guid Id, string ParcelCode, Guid LockerId, string LockerCode, string LockerAddress,
        Guid? CompartmentId, string? CompartmentCode,
        DateTimeOffset StoredAt, DateTimeOffset PickupDueAt, DateTimeOffset MaxStorageUntil,
        string? ResidentName, string? ResidentPhone, string RecoveryAddress);

    private sealed class ScopedIncident
    {
        public Guid Id { get; init; }
        public Guid? LockerId { get; init; }
        public string? LockerCode { get; init; }
        public Guid? CompartmentId { get; init; }
        public string? CompartmentCode { get; init; }
        public string Type { get; init; } = string.Empty;
        public IncidentSource Source { get; init; }
        public IncidentStatus Status { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public Guid? AssignedOperatorUserId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
