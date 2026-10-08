using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/operational-records"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class OperationalRecordsController(IOperationalRecordService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? query, [FromQuery] Guid? lockerId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!TryGetIdentity(out Guid userId, out string role)) return Unauthorized();
        return Ok(await service.SearchAsync(userId, role, query, lockerId, ct, pageNumber, pageSize));
    }

    private bool TryGetIdentity(out Guid userId, out string role)
    {
        role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
