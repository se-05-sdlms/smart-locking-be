using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/emergency-unlocks"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class EmergencyUnlocksController(IEmergencyUnlockService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(EmergencyUnlockRequest request, CancellationToken ct)
    {
        if (!TryGetIdentity(out Guid userId, out string role)) return Unauthorized();
        return StatusCode(201, await service.CreateAsync(userId, role, request, ct));
    }

    private bool TryGetIdentity(out Guid userId, out string role)
    {
        role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
