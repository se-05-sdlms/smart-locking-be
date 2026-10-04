using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Dashboards;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ApiPolicies.Administrator)]
public class AdminController(
    IAdminService adminService,
    ISystemPolicyService systemPolicyService) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] GetDashboardOverviewRequest? request,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => adminService.GetDashboardOverviewAsync(request, cancellationToken));
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] GetSystemStatisticsRequest? request,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => adminService.GetSystemStatisticsAsync(request, cancellationToken));
    }

    [HttpGet("system-policy")]
    public async Task<IActionResult> GetSystemPolicy(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => systemPolicyService.GetActivePolicyAsync(cancellationToken));
    }

    [HttpPut("system-policy")]
    public async Task<IActionResult> UpdateSystemPolicy(
        [FromBody] UpdateSystemPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid adminId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        return await ExecuteAsync(() => systemPolicyService.UpdatePolicyAsync(
            adminId,
            request,
            GetIpAddress(),
            cancellationToken));
    }

    private bool TryGetUserId(out Guid userId)
    {
        string? claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private string? GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<IActionResult> ExecuteAsync<TResponse>(Func<Task<TResponse>> action)
    {
        try
        {
            TResponse result = await action();
            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unauthorized(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
