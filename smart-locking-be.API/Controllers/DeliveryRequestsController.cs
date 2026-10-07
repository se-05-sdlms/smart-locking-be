using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Authorization;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/delivery-requests")]
[EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
public sealed class DeliveryRequestsController(IDeliveryRequestService deliveryRequestService) : ControllerBase
{
    private const string GuestSessionHeaderName = "X-Guest-Session-Token";

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create(
        [FromBody] InitiateDeliveryRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            async () => StatusCode(
                StatusCodes.Status201Created,
                await deliveryRequestService.CreateAsync(request, cancellationToken)));

    [HttpPost("{id:guid}:submit")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Upload)]
    [RequestTimeout(RequestTimeoutPolicyNames.Upload)]
    public async Task<IActionResult> Submit(
        Guid id,
        [FromBody] SubmitDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            return Unauthorized(new { message = $"Thiếu {GuestSessionHeaderName}." });
        }

        return await ExecuteAsync(() => deliveryRequestService.SubmitAsync(id, token, request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetGuestStatus(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            return Unauthorized(new { message = $"Thiếu {GuestSessionHeaderName}." });
        }

        return await ExecuteAsync(() => deliveryRequestService.GetAsync(id, token, cancellationToken));
    }

    // ==========================================
    // Issue #20: Resident Approval Flow Endpoints
    // ==========================================

    [HttpGet]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> GetPendingRequests(
        [FromQuery] string status = "pending",
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Hiện chỉ hỗ trợ lọc status=pending." });
        }
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => deliveryRequestService.GetPendingRequestsForResidentAsync(userId, cancellationToken));
    }

    [HttpPost("{id:guid}:approve")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> ApproveRequest(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => deliveryRequestService.ApproveDeliveryRequestAsync(userId, id, cancellationToken));
    }

    [HttpPost("{id:guid}:reject")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> RejectRequest(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => deliveryRequestService.RejectDeliveryRequestAsync(userId, id, cancellationToken));
    }

    [HttpPost("{id:guid}:openCompartment")]
    [AllowAnonymous]
    public async Task<IActionResult> OpenCompartment(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            return Unauthorized(new { message = $"Thiếu {GuestSessionHeaderName}." });
        }

        return await ExecuteAsync(() => deliveryRequestService.OpenCompartmentAsync(id, token, cancellationToken));
    }

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
