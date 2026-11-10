using smart_locking_be.Application.DTOs.Dashboards;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IAdminService
{
    Task<AdminDashboardResponse> GetDashboardOverviewAsync(
        GetDashboardOverviewRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<SystemStatisticsResponse> GetSystemStatisticsAsync(
        GetSystemStatisticsRequest? request = null,
        CancellationToken cancellationToken = default);
}
