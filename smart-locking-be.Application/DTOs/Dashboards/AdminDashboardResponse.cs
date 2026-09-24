namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record AdminDashboardResponse(
    SystemStatisticsResponse Statistics,
    LockerStatusDistributionResponse LockerStatus,
    CompartmentStatusDistributionResponse CompartmentStatus,
    IReadOnlyList<DailyParcelStatisticResponse> RecentParcelsTrend,
    IReadOnlyList<AttentionLockerResponse> AttentionLockers,
    IReadOnlyList<RecentLockerResponse> RecentLockers,
    IReadOnlyList<RecentAuditLogResponse> RecentAuditLogs
);
