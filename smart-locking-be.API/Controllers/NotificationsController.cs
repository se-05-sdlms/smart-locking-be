using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await notificationService.GetForUserAsync(
            GetCurrentUserId(), unreadOnly, limit, cancellationToken));

    [HttpPatch("{id:guid}/read")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        Ok(await notificationService.MarkReadAsync(GetCurrentUserId(), id, cancellationToken));

    [HttpPatch("read-all")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
        Ok(await notificationService.MarkAllReadAsync(GetCurrentUserId(), cancellationToken));

    [HttpPost("rules")]
    [Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> CreateRule(
        CreateNotificationRuleRequest request,
        CancellationToken cancellationToken)
    {
        NotificationRuleResponse response = await notificationService.CreateRuleAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    private Guid GetCurrentUserId()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            throw new UnauthorizedException("Invalid authentication token.");
        return userId;
    }
}
