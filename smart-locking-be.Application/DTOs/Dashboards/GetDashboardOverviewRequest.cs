namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record GetDashboardOverviewRequest(
    int TrendDays = 7,
    int RecentLimit = 5
);
