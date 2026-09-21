using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize(Policy = ApiPolicies.LockerOperator)]
public sealed class IncidentsController(IOperatorService operatorService) : ControllerBase
{
    /// <summary>Danh sách sự cố của các tủ được phân công.</summary>
    [HttpGet]
    public async Task<IActionResult> GetIncidents(
        [FromQuery] Guid? lockerId, [FromQuery] IncidentStatus? status,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid operatorId))
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });

        try
        {
            return Ok(await operatorService.GetIncidentsAsync(
                operatorId, lockerId, status, pageNumber, pageSize, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
