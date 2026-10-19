using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Dashboards;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ApiPolicies.Administrator)]
public class AdminController(IAdminService adminService) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] GetDashboardOverviewRequest? request,
        CancellationToken cancellationToken)
    {
        return Ok(await adminService.GetDashboardOverviewAsync(request, cancellationToken));
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] GetSystemStatisticsRequest? request,
        CancellationToken cancellationToken)
    {
        return Ok(await adminService.GetSystemStatisticsAsync(request, cancellationToken));
    }
}
