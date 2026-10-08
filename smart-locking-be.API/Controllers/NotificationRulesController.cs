using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/notification-rules")]
[Authorize(Policy = ApiPolicies.Administrator)]
public sealed class NotificationRulesController(INotificationRuleService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? systemPolicyId, [FromQuery] bool? isEnabled, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(systemPolicyId, isEnabled, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId)) return Unauthorized();
        NotificationRuleResponse response = await service.CreateAsync(userId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateNotificationRuleRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId)) return Unauthorized();
        return Ok(await service.UpdateAsync(userId, id, request, cancellationToken));
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
