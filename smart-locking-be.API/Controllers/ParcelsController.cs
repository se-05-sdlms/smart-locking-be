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
[Authorize]
public sealed class ParcelsController(IParcelService parcelService) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Resident,LockerOperator")]
    public async Task<IActionResult> GetParcels(
        [FromQuery] ParcelListView view = ParcelListView.Active,
        [FromQuery] string? search = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetParcelsAsync(userId, role, view, search, from, to, cancellationToken, pageNumber, pageSize));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Resident,LockerOperator")]
    public async Task<IActionResult> GetParcel(Guid id, CancellationToken cancellationToken) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetParcelAsync(userId, role, id, cancellationToken));

    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = "Resident,LockerOperator")]
    public async Task<IActionResult> GetHistory(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetHistoryAsync(userId, role, id, cancellationToken, pageNumber, pageSize));

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

    [HttpPost("{id:guid}:transferToCollectionPoint")]
    [Authorize(Roles = "Administrator,LockerOperator")]
    public async Task<IActionResult> TransferToCollectionPoint(Guid id, CancellationToken cancellationToken) =>
        await ExecuteAsync((userId, role) =>
            parcelService.TransferOverdueAsync(userId, role, id, cancellationToken));

    private async Task<IActionResult> ExecuteAsync<TResponse>(
        Func<Guid, string, Task<TResponse>> action)
    {
        string? idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? role = User.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(idClaim, out Guid userId) || string.IsNullOrWhiteSpace(role))
        {
            return Unauthorized(new { message = "Invalid authentication token." });
        }

        return Ok(await action(userId, role));
    }
}
