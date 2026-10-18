using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/resident-subscriptions"), Authorize(Policy = ApiPolicies.Resident)]
public sealed class ResidentSubscriptionsController(IResidentSubscriptionService service) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken) =>
        Ok(await service.GetCurrentAsync(User.GetRequiredUserId(), cancellationToken));

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken) =>
        Ok(await service.GetHistoryAsync(User.GetRequiredUserId(), cancellationToken));

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(SubscribePlanRequest request, CancellationToken cancellationToken) =>
        Ok(await service.SubscribeAsync(User.GetRequiredUserId(), request, cancellationToken));
}
