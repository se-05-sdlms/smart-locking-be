using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/parcels")]
[Authorize(Roles = "Resident,LockerOperator")]
public sealed class ParcelsController(IParcelService parcelService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetParcels(
        [FromQuery] ParcelListView view = ParcelListView.Active,
        [FromQuery] string? search = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetParcelsAsync(userId, role, view, search, from, to, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetParcel(Guid id, CancellationToken cancellationToken) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetParcelAsync(userId, role, id, cancellationToken));

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetHistoryAsync(userId, role, id, cancellationToken));

    [HttpPost("{id:guid}:openCompartment")]
    [Authorize(Roles = "Resident")]
    [EnableRateLimiting(RateLimitPolicyNames.DeviceCommand)]
    [RequestTimeout(RequestTimeoutPolicyNames.DeviceCommand)]
    public async Task<IActionResult> OpenCompartment(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        try
        {
            PickupUnlockResponse response = await parcelService.OpenCompartmentAsync(
                userId,
                id,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                cancellationToken);
            return response.Result switch
            {
                LockerAccessResult.Succeeded => Accepted(response),
                LockerAccessResult.Blocked => Conflict(response),
                _ => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            };
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task<IActionResult> ExecuteAsync<TResponse>(
        Func<Guid, string, Task<TResponse>> action)
    {
        string? idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? role = User.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(idClaim, out Guid userId) || string.IsNullOrWhiteSpace(role))
        {
            return Unauthorized(new { message = "Invalid authentication token." });
        }

        try
        {
            return Ok(await action(userId, role));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }
}
