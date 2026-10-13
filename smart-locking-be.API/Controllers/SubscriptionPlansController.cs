using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
public sealed class SubscriptionPlansController(ISubscriptionPlanService service) : ControllerBase
{
    [HttpGet("api/subscription-plans"), AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Ok(await service.GetPlansAsync(true, cancellationToken));

    [HttpGet("api/admin/subscription-plans"), Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) => Ok(await service.GetPlansAsync(false, cancellationToken));

    [HttpPost("api/admin/subscription-plans"), Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> Create(CreateSubscriptionPlanRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await service.CreateAsync(request, cancellationToken));

    [HttpPut("api/admin/subscription-plans/{id:guid}"), Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> Update(Guid id, UpdateSubscriptionPlanRequest request, CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, request, cancellationToken));
}
