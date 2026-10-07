using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Authorization;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/delivery-requests")]
[EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
public sealed class DeliveryRequestsController(IDeliveryRequestService deliveryRequestService) : ControllerBase
{
    private const string GuestSessionHeaderName = "X-Guest-Session-Token";

    // ==========================================
    // Issue #19: Shipper Guest Flow Endpoints
    // ==========================================

    [HttpPost("initiate")]
    [AllowAnonymous]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiateDeliveryRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            async () => StatusCode(
                StatusCodes.Status201Created,
                await deliveryRequestService.InitiateAsync(request, cancellationToken)));

    [HttpPost("{id:guid}/upload-image")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Upload)]
    [RequestTimeout(RequestTimeoutPolicyNames.Upload)]
    public async Task<IActionResult> UploadImage(
        Guid id,
        [FromBody] UploadParcelImageRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            return Unauthorized(new { message = $"Thiếu {GuestSessionHeaderName}." });
        }

        return await ExecuteAsync(() => deliveryRequestService.UploadImageAsync(id, token, request, cancellationToken));
    }

    [HttpPost("{id:guid}/submit-recipient")]
    [AllowAnonymous]
    public async Task<IActionResult> SubmitRecipient(
        Guid id,
        [FromBody] SubmitRecipientPhoneRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            return Unauthorized(new { message = $"Thiếu {GuestSessionHeaderName}." });
        }

        return await ExecuteAsync(() => deliveryRequestService.SubmitRecipientAsync(id, token, request, cancellationToken));
    }

    // ==========================================
    // Issue #20: Resident Approval Flow Endpoints
    // ==========================================

    [HttpGet("pending")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> GetPendingRequests(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => deliveryRequestService.GetPendingRequestsForResidentAsync(userId, cancellationToken));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> ApproveRequest(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => deliveryRequestService.ApproveDeliveryRequestAsync(userId, id, cancellationToken));
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> RejectRequest(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => deliveryRequestService.RejectDeliveryRequestAsync(userId, id, cancellationToken));
    }

    [HttpPost("{id:guid}/open-compartment")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.DeviceCommand)]
    [RequestTimeout(RequestTimeoutPolicyNames.DeviceCommand)]
    public async Task<IActionResult> OpenCompartment(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            return Unauthorized(new { message = $"Thiếu {GuestSessionHeaderName}." });
        }

        return await ExecuteAsync<IActionResult>(async () =>
        {
            OpenCompartmentResponse response = await deliveryRequestService.OpenCompartmentAsync(
                id, token, HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), cancellationToken);
            return response.Result switch
            {
                LockerAccessResult.Succeeded => Accepted(response),
                LockerAccessResult.Blocked => Conflict(response),
                _ => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            };
        });
    }

    // ==========================================
    // Helpers
    // ==========================================

    private bool TryGetGuestSessionToken(out string token)
    {
        token = Request.Headers[GuestSessionHeaderName].FirstOrDefault()?.Trim() ?? string.Empty;
        return token.Length > 0;
    }

    private bool TryGetUserId(out Guid userId)
    {
        string? claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private async Task<IActionResult> ExecuteAsync<TResponse>(Func<Task<TResponse>> action)
    {
        try
        {
            TResponse result = await action();
            return result is IActionResult actionResult ? actionResult : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unauthorized(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (TimeoutException exception)
        {
            return StatusCode(StatusCodes.Status410Gone, new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
