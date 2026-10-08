using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/notification-rules")]
[Authorize(Policy = ApiPolicies.Administrator)]
public sealed class NotificationRulesController(INotificationRuleService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken)
    {
        NotificationRuleResponse response = await service.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
