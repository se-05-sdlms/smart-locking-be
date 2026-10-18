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
        var (userId, role) = User.GetRequiredIdentity();
        return StatusCode(201, await service.CreateAsync(userId, role, request, ct));
    }
}
