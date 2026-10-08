using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/operational-records"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class OperationalRecordsController(IOperationsService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? query, [FromQuery] Guid? lockerId, CancellationToken ct)
    {
        if (!TryGetIdentity(out Guid userId, out string role)) return Unauthorized();
        try { return Ok(await service.SearchAsync(userId, role, query, lockerId, ct)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    private bool TryGetIdentity(out Guid userId, out string role)
    {
        role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
