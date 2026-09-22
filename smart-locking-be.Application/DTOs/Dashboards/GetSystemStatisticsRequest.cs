namespace smart_locking_be.Application.DTOs.Dashboards;

public sealed record GetSystemStatisticsRequest(
    int OverdueFeeDays = 30
);
