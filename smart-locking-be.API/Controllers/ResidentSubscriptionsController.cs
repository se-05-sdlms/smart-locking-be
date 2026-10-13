using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/resident-subscriptions"), Authorize(Policy = ApiPolicies.Resident)]
public sealed class ResidentSubscriptionsController(IResidentSubscriptionService service) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken) => TryGetUserId(out var userId)
        ? Ok(await service.GetCurrentAsync(userId, cancellationToken)) : Unauthorized();

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken) => TryGetUserId(out var userId)
        ? Ok(await service.GetHistoryAsync(userId, cancellationToken)) : Unauthorized();

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(SubscribePlanRequest request, CancellationToken cancellationToken) => TryGetUserId(out var userId)
        ? Ok(await service.SubscribeAsync(userId, request, cancellationToken)) : Unauthorized();

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
