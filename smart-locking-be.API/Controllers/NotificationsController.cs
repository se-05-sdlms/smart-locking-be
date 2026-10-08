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
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        await ExecuteForCurrentUserAsync(userId =>
            notificationService.GetForUserAsync(userId, unreadOnly, pageSize, cancellationToken, pageNumber));

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateNotificationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteForCurrentUserAsync(userId =>
            notificationService.SetReadStateAsync(userId, id, request.IsRead, cancellationToken));

    [HttpPost("/api/notifications:markAllRead")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
        await ExecuteForCurrentUserAsync(userId =>
            notificationService.MarkAllReadAsync(userId, cancellationToken));

    private async Task<IActionResult> ExecuteForCurrentUserAsync<TResponse>(
        Func<Guid, Task<TResponse>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            return Unauthorized(new { message = "Invalid authentication token." });
        }

        return Ok(await action(userId));
    }
}
