using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/parcels")]
[Authorize]
public sealed class ParcelsController(
    IParcelService parcelService,
    IParcelPickupService parcelPickupService) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Resident,LockerOperator,Administrator")]
    public async Task<IActionResult> GetParcels(
        [FromQuery] ParcelListView view = ParcelListView.Active,
        [FromQuery] string? search = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var (userId, role) = GetIdentity();
        return Ok(await parcelService.GetParcelsAsync(userId, role, view, search, from, to, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Resident,LockerOperator")]
    public async Task<IActionResult> GetParcel(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = GetIdentity();
        return Ok(await parcelService.GetParcelAsync(userId, role, id, cancellationToken));
    }

    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = "Resident,LockerOperator")]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = GetIdentity();
        return Ok(await parcelService.GetHistoryAsync(userId, role, id, cancellationToken));
    }

    [HttpPost("{id:guid}/unlock-pickup")]
    [Authorize(Roles = "Resident")]
    [EnableRateLimiting(RateLimitPolicyNames.DeviceCommand)]
    [RequestTimeout(RequestTimeoutPolicyNames.DeviceCommand)]
    public async Task<IActionResult> UnlockPickup(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        PickupUnlockResponse response = await parcelPickupService.UnlockAsync(
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

    private (Guid UserId, string Role) GetIdentity()
    {
        string? idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? role = User.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(idClaim, out Guid userId) || string.IsNullOrWhiteSpace(role))
        {
            throw new UnauthorizedException("Invalid authentication token.");
        }

        return (userId, role);
    }
    [HttpPost("{id:guid}:transferToCollectionPoint")]
    [Authorize(Roles = "Administrator,LockerOperator")]
    public async Task<IActionResult> TransferToCollectionPoint(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = GetIdentity();
        return Ok(await parcelService.TransferOverdueAsync(userId, role, id, cancellationToken));
    }
}
