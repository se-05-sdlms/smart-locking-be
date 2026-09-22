using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Dashboards;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class AdminService(ApplicationDbContext dbContext) : IAdminService
{
    public async Task<AdminDashboardResponse> GetDashboardOverviewAsync(
        GetDashboardOverviewRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset thirtyDaysAgo = now.AddDays(-30);

        int trendDays = request?.TrendDays is > 0 and <= 30 ? request.TrendDays : 7;
        int recentLimit = request?.RecentLimit is > 0 and <= 50 ? request.RecentLimit : 5;
        DateTimeOffset trendStartDate = todayStart.AddDays(-(trendDays - 1));

        // 1. Users Statistics
        int totalUsers = await dbContext.Users.CountAsync(cancellationToken);
        int residentCount = await dbContext.Users.CountAsync(u => u.Role == UserRole.Resident, cancellationToken);
        int operatorCount = await dbContext.Users.CountAsync(u => u.Role == UserRole.LockerOperator, cancellationToken);

        // 2. Lockers Statistics
        var lockers = await dbContext.Lockers
            .AsNoTracking()
            .Include(l => l.OperatorAssignments)
                .ThenInclude(a => a.OperatorUser)
                    .ThenInclude(u => u!.ResidentProfile)
            .ToListAsync(cancellationToken);

        int totalLockers = lockers.Count;
        int activeLockers = lockers.Count(l => l.OperationalStatus == LockerOperationalStatus.Operational);
        int maintenanceLockers = lockers.Count(l => l.OperationalStatus == LockerOperationalStatus.OutOfService);
        int suspendedLockers = lockers.Count(l => l.OperationalStatus == LockerOperationalStatus.Inactive);
        int offlineLockers = lockers.Count(l => l.ConnectionStatus == LockerConnectionStatus.Offline);

        var attentionLockers = lockers
            .Where(l => l.ConnectionStatus == LockerConnectionStatus.Offline || l.OperationalStatus != LockerOperationalStatus.Operational)
            .Select(l => new AttentionLockerResponse(
                l.Id,
                l.Code,
                l.Address,
                l.ConnectionStatus,
                l.OperationalStatus
            ))
            .ToList();

        var recentLockers = lockers
            .OrderByDescending(l => l.UpdatedAt)
            .ThenByDescending(l => l.CreatedAt)
            .Take(recentLimit)
            .Select(l =>
            {
                var activeAssignment = l.OperatorAssignments.FirstOrDefault(a => a.RevokedAt == null);
                string? operatorName = activeAssignment?.OperatorUser?.ResidentProfile?.FullName
                    ?? activeAssignment?.OperatorUser?.PhoneNumber
                    ?? activeAssignment?.OperatorUser?.Email;

                return new RecentLockerResponse(
                    l.Id,
                    l.Code,
                    l.Address,
                    operatorName,
                    l.ConnectionStatus,
                    l.OperationalStatus,
                    l.UpdatedAt
                );
            })
            .ToList();

        // 3. Compartments & Parcels Statistics
        var activeParcels = await dbContext.Parcels
            .AsNoTracking()
            .Include(p => p.DeliveryRequest)
            .Where(p => p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue)
            .ToListAsync(cancellationToken);

        var occupiedCompartmentIds = activeParcels
            .Where(p => p.DeliveryRequest != null && p.DeliveryRequest.AllocatedCompartmentId.HasValue)
            .Select(p => p.DeliveryRequest!.AllocatedCompartmentId!.Value)
            .ToHashSet();

        var overdueCompartmentIds = activeParcels
            .Where(p => p.Status == ParcelStatus.Overdue && p.DeliveryRequest != null && p.DeliveryRequest.AllocatedCompartmentId.HasValue)
            .Select(p => p.DeliveryRequest!.AllocatedCompartmentId!.Value)
            .ToHashSet();

        var compartments = await dbContext.LockerCompartments
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        int totalCompartments = compartments.Count;
        int maintenanceCompartments = compartments.Count(c => c.OperationalStatus == LockerCompartmentOperationalStatus.OutOfService);
        int disabledCompartments = compartments.Count(c => c.OperationalStatus == LockerCompartmentOperationalStatus.Inactive);
        int overdueCompartments = compartments.Count(c => overdueCompartmentIds.Contains(c.Id));
        int occupiedCompartments = compartments.Count(c => occupiedCompartmentIds.Contains(c.Id) && !overdueCompartmentIds.Contains(c.Id));
        int availableCompartments = compartments.Count(c =>
            c.OperationalStatus == LockerCompartmentOperationalStatus.Operational && !occupiedCompartmentIds.Contains(c.Id));

        int storedParcelsCount = activeParcels.Count;
        int overdueParcelsCount = activeParcels.Count(p => p.Status == ParcelStatus.Overdue);

        int newParcelsTodayCount = await dbContext.Parcels
            .AsNoTracking()
            .CountAsync(p => p.StoredAt >= todayStart, cancellationToken);

        // 4. Overdue Charges (last 30 days)
        decimal totalOverdueFeeAmount = await dbContext.OverdueCharges
            .AsNoTracking()
            .Where(c => c.CreatedAt >= thirtyDaysAgo)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

        // 5. Incidents Statistics
        var incidents = await dbContext.Incidents
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        int openIncidentsCount = incidents.Count(i =>
            i.Status == IncidentStatus.Open ||
            i.Status == IncidentStatus.Investigating ||
            i.Status == IncidentStatus.Escalated);

        int highPriorityIncidentsCount = incidents.Count(i => i.Status == IncidentStatus.Escalated);

        // 6. Recent Parcels Trend
        var recentParcels = await dbContext.Parcels
            .AsNoTracking()
            .Where(p => p.StoredAt >= trendStartDate && p.StoredAt <= now)
            .ToListAsync(cancellationToken);

        var dailyCounts = recentParcels
            .GroupBy(p => DateOnly.FromDateTime(p.StoredAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());

        var parcelsTrend = new List<DailyParcelStatisticResponse>();
        for (int i = 0; i < trendDays; i++)
        {
            var targetDate = DateOnly.FromDateTime(trendStartDate.AddDays(i).UtcDateTime);
            dailyCounts.TryGetValue(targetDate, out int count);
            parcelsTrend.Add(new DailyParcelStatisticResponse(
                targetDate,
                $"{targetDate.Day}/{targetDate.Month}",
                count
            ));
        }

        // 7. Recent Audit Logs
        var recentLogs = await dbContext.AuditLogs
            .AsNoTracking()
            .Include(l => l.ActorUser)
                .ThenInclude(u => u!.ResidentProfile)
            .OrderByDescending(l => l.OccurredAt)
            .Take(recentLimit)
            .ToListAsync(cancellationToken);

        var recentAuditLogs = recentLogs.Select(l =>
        {
            string actorName = l.ActorUser?.ResidentProfile?.FullName
                ?? l.ActorUser?.PhoneNumber
                ?? l.ActorUser?.Email
                ?? "Hệ thống";

            string target = l.EntityType ?? l.EntityId?.ToString() ?? "Hệ thống";

            return new RecentAuditLogResponse(
                l.Id,
                l.Action,
                actorName,
                target,
                l.Result,
                l.OccurredAt
            );
        }).ToList();

        // Assemble Final Response
        var statistics = new SystemStatisticsResponse(
            TotalLockers: totalLockers,
            ActiveLockers: activeLockers,
            TotalCompartments: totalCompartments,
            AvailableCompartments: availableCompartments,
            OccupiedCompartments: occupiedCompartments + overdueCompartments,
            TotalUsers: totalUsers,
            ResidentCount: residentCount,
            OperatorCount: operatorCount,
            StoredParcelsCount: storedParcelsCount,
            NewParcelsTodayCount: newParcelsTodayCount,
            OverdueParcelsCount: overdueParcelsCount,
            OfflineLockersCount: offlineLockers,
            OpenIncidentsCount: openIncidentsCount,
            HighPriorityIncidentsCount: highPriorityIncidentsCount,
            TotalOverdueFeeAmountLast30Days: totalOverdueFeeAmount
        );

        var lockerStatusDistribution = new LockerStatusDistributionResponse(
            Total: totalLockers,
            Operational: activeLockers,
            Maintenance: maintenanceLockers,
            Suspended: suspendedLockers,
            Offline: offlineLockers
        );

        var compartmentStatusDistribution = new CompartmentStatusDistributionResponse(
            Total: totalCompartments,
            Available: availableCompartments,
            Occupied: occupiedCompartments,
            Overdue: overdueCompartments,
            Maintenance: maintenanceCompartments,
            Disabled: disabledCompartments
        );

        return new AdminDashboardResponse(
            Statistics: statistics,
            LockerStatus: lockerStatusDistribution,
            CompartmentStatus: compartmentStatusDistribution,
            RecentParcelsTrend: parcelsTrend,
            AttentionLockers: attentionLockers,
            RecentLockers: recentLockers,
            RecentAuditLogs: recentAuditLogs
        );
    }

    public async Task<SystemStatisticsResponse> GetSystemStatisticsAsync(
        GetSystemStatisticsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);

        int overdueDays = request?.OverdueFeeDays is > 0 and <= 365 ? request.OverdueFeeDays : 30;
        DateTimeOffset overdueChargesStartDate = now.AddDays(-overdueDays);

        int totalUsers = await dbContext.Users.CountAsync(cancellationToken);
        int residentCount = await dbContext.Users.CountAsync(u => u.Role == UserRole.Resident, cancellationToken);
        int operatorCount = await dbContext.Users.CountAsync(u => u.Role == UserRole.LockerOperator, cancellationToken);

        int totalLockers = await dbContext.Lockers.CountAsync(cancellationToken);
        int activeLockers = await dbContext.Lockers.CountAsync(l => l.OperationalStatus == LockerOperationalStatus.Operational, cancellationToken);
        int offlineLockers = await dbContext.Lockers.CountAsync(l => l.ConnectionStatus == LockerConnectionStatus.Offline, cancellationToken);

        int totalCompartments = await dbContext.LockerCompartments.CountAsync(cancellationToken);

        var activeParcels = await dbContext.Parcels
            .AsNoTracking()
            .Include(p => p.DeliveryRequest)
            .Where(p => p.Status == ParcelStatus.Stored || p.Status == ParcelStatus.Overdue)
            .ToListAsync(cancellationToken);

        int storedParcelsCount = activeParcels.Count;
        int overdueParcelsCount = activeParcels.Count(p => p.Status == ParcelStatus.Overdue);

        var occupiedCompartmentIds = activeParcels
            .Where(p => p.DeliveryRequest != null && p.DeliveryRequest.AllocatedCompartmentId.HasValue)
            .Select(p => p.DeliveryRequest!.AllocatedCompartmentId!.Value)
            .ToHashSet();

        int occupiedCompartments = occupiedCompartmentIds.Count;
        int availableCompartments = await dbContext.LockerCompartments
            .CountAsync(c => c.OperationalStatus == LockerCompartmentOperationalStatus.Operational && !occupiedCompartmentIds.Contains(c.Id), cancellationToken);

        int newParcelsTodayCount = await dbContext.Parcels
            .AsNoTracking()
            .CountAsync(p => p.StoredAt >= todayStart, cancellationToken);

        decimal totalOverdueFeeAmount = await dbContext.OverdueCharges
            .AsNoTracking()
            .Where(c => c.CreatedAt >= overdueChargesStartDate)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

        int openIncidentsCount = await dbContext.Incidents
            .AsNoTracking()
            .CountAsync(i =>
                i.Status == IncidentStatus.Open ||
                i.Status == IncidentStatus.Investigating ||
                i.Status == IncidentStatus.Escalated, cancellationToken);

        int highPriorityIncidentsCount = await dbContext.Incidents
            .AsNoTracking()
            .CountAsync(i => i.Status == IncidentStatus.Escalated, cancellationToken);

        return new SystemStatisticsResponse(
            TotalLockers: totalLockers,
            ActiveLockers: activeLockers,
            TotalCompartments: totalCompartments,
            AvailableCompartments: availableCompartments,
            OccupiedCompartments: occupiedCompartments,
            TotalUsers: totalUsers,
            ResidentCount: residentCount,
            OperatorCount: operatorCount,
            StoredParcelsCount: storedParcelsCount,
            NewParcelsTodayCount: newParcelsTodayCount,
            OverdueParcelsCount: overdueParcelsCount,
            OfflineLockersCount: offlineLockers,
            OpenIncidentsCount: openIncidentsCount,
            HighPriorityIncidentsCount: highPriorityIncidentsCount,
            TotalOverdueFeeAmountLast30Days: totalOverdueFeeAmount
        );
    }
}
