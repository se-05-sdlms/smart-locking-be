using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/emergency-unlocks"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class EmergencyUnlocksController(IOperationsService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(EmergencyUnlockRequest request, CancellationToken ct)
    {
        if (!TryGetIdentity(out Guid userId, out string role)) return Unauthorized();
        try { return StatusCode(201, await service.EmergencyUnlockAsync(userId, role, request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    private bool TryGetIdentity(out Guid userId, out string role)
    {
        role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
