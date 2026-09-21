using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/operator")]
[Authorize(Policy = ApiPolicies.LockerOperator)]
public sealed class OperatorController(IOperatorService operatorService) : ControllerBase
{
    /// <summary>Tổng quan vận hành các tủ đang được phân công cho Operator hiện tại.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid operatorId))
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });

        return Ok(await operatorService.GetDashboardAsync(operatorId, cancellationToken));
    }
}
