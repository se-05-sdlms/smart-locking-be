using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/lockers/{lockerId:guid}/location"), Authorize(Roles = "Administrator")]
public sealed class LockerLocationsController(IOperatorCoverageService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid lockerId, CancellationToken ct) => Ok(await service.GetLocationAsync(lockerId, ct));

    [HttpPut]
    public async Task<IActionResult> Confirm(Guid lockerId, ConfirmLockerLocationRequest request, CancellationToken ct) =>
        Ok(await service.ConfirmLocationAsync(User.GetRequiredUserId(), lockerId, request, ct));
}
