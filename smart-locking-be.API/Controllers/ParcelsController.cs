using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.API.Middleware;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;

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
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await parcelService.GetParcelsAsync(userId, role, view, search, from, to, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Resident,LockerOperator,Administrator")]
    public async Task<IActionResult> GetParcel(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await parcelService.GetParcelAsync(userId, role, id, cancellationToken));
    }

    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = "Resident,LockerOperator,Administrator")]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await parcelService.GetHistoryAsync(userId, role, id, cancellationToken));
    }

    [HttpPost("{id:guid}/unlock-pickup")]
    [Authorize(Roles = "Resident")]
    [EnableRateLimiting(RateLimitPolicyNames.DeviceCommand)]
    [RequestTimeout(RequestTimeoutPolicyNames.DeviceCommand)]
    public async Task<IActionResult> UnlockPickup(Guid id, CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        PickupUnlockResponse response = await parcelPickupService.UnlockAsync(
            userId,
            id,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return response.Result switch
        {
            LockerAccessResult.Succeeded => Accepted(response),
            LockerAccessResult.Blocked => new ObjectResult(ProblemResponse.Create(HttpContext,
                StatusCodes.Status409Conflict, LockerErrorCodes.OpenFailed,
                "Locker compartment could not be opened."))
            { StatusCode = StatusCodes.Status409Conflict, ContentTypes = { "application/problem+json" } },
            _ => new ObjectResult(ProblemResponse.Create(HttpContext,
                StatusCodes.Status503ServiceUnavailable, LockerErrorCodes.OpenFailed,
                "Locker compartment could not be opened."))
            { StatusCode = StatusCodes.Status503ServiceUnavailable, ContentTypes = { "application/problem+json" } },
        };
    }

    // Chỉ đọc kết quả đã đồng bộ từ ESP; không coi nút xác nhận là bằng chứng cửa đã mở/đóng.
    [HttpPost("{id:guid}/confirm-pickup")]
    [Authorize(Roles = "Resident")]
    public async Task<IActionResult> ConfirmPickup(Guid id, [FromBody] ConfirmPickupRequest request, CancellationToken cancellationToken)
    {
        var response = await parcelPickupService.ConfirmPickupAsync(User.GetRequiredUserId(), request.AccessEventId, cancellationToken);
        if (response.ParcelId != id)
            throw new BadRequestException(ParcelErrorCodes.InvalidAccessEvent, "Lượt mở ngăn không thuộc kiện hàng được yêu cầu.");
        return Ok(response);
    }

    [HttpPost("{id:guid}:transferToCollectionPoint")]
    [Authorize(Roles = "Administrator,LockerOperator")]
    public async Task<IActionResult> TransferToCollectionPoint(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = User.GetRequiredIdentity();
        var response = await parcelService.TransferOverdueAsync(userId, role, id, cancellationToken);
        return response.ResetStatus == "Pending" ? Accepted(response) : Ok(response);
    }
}
