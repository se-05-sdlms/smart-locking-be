using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await notificationService.GetForUserAsync(
            User.GetRequiredUserId(), unreadOnly, limit, cancellationToken));

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        Ok(await notificationService.MarkReadAsync(User.GetRequiredUserId(), id, cancellationToken));

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
        Ok(await notificationService.MarkAllReadAsync(User.GetRequiredUserId(), cancellationToken));

    [HttpPost("rules")]
    [Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> CreateRule(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken)
    {
        NotificationRuleResponse response = await notificationService.CreateRuleAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
