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
        StatusCode(StatusCodes.Status201Created,
            await deliveryRequestService.CreateAsync(request, cancellationToken));

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
            throw new UnauthorizedException($"Thiếu {GuestSessionHeaderName}.");
        }

        return Ok(await deliveryRequestService.SubmitAsync(id, token, request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetGuestStatus(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            throw new UnauthorizedException($"Thiếu {GuestSessionHeaderName}.");
        }

        return Ok(await deliveryRequestService.GetAsync(id, token, cancellationToken));
    }

    // ==========================================
    // Issue #20: Resident Approval Flow Endpoints
    // ==========================================

    [HttpGet]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> GetPendingRequests(
        [FromQuery] string status = "pending",
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Hiện chỉ hỗ trợ lọc status=pending.");
        }
        Guid userId = User.GetRequiredUserId();

        return Ok(await deliveryRequestService.GetPendingRequestsForResidentAsync(
            userId, cancellationToken, pageNumber, pageSize));
    }

    [HttpPost("{id:guid}:approve")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> ApproveRequest(Guid id, CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        return Ok(await deliveryRequestService.ApproveDeliveryRequestAsync(userId, id, cancellationToken));
    }

    [HttpPost("{id:guid}:reject")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> RejectRequest(Guid id, CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        return Ok(await deliveryRequestService.RejectDeliveryRequestAsync(userId, id, cancellationToken));
    }

    [HttpPost("{id:guid}:openCompartment")]
    [AllowAnonymous]
    public async Task<IActionResult> OpenCompartment(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            throw new UnauthorizedException($"Thiếu {GuestSessionHeaderName}.");
        }

        return Ok(await deliveryRequestService.OpenCompartmentAsync(id, token, cancellationToken));
    }

    private bool TryGetGuestSessionToken(out string token)
    {
        token = Request.Headers[GuestSessionHeaderName].FirstOrDefault()?.Trim() ?? string.Empty;
        return token.Length > 0;
    }
}
