using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/notification-rules")]
[Authorize(Policy = ApiPolicies.Administrator)]
public sealed class NotificationRulesController(INotificationService notificationService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            NotificationRuleResponse response = await notificationService.CreateRuleAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
