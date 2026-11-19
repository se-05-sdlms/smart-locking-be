using smart_locking_be.Application.DTOs.Dashboards;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IAdminForecastService
{
    Task<AdminForecastResponse> GetForecastAsync(
        int horizonDays = 7,
        CancellationToken cancellationToken = default);
}
