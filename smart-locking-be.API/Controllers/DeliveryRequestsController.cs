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
    private const int MaxWaybillImageBytes = 8 * 1024 * 1024;

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
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, $"{GuestSessionHeaderName} is required.");
        }

        return Ok(await deliveryRequestService.SubmitAsync(id, token, request, cancellationToken));
    }

    [HttpPost("{id:guid}:waybill-ocr")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Upload)]
    [RequestTimeout(RequestTimeoutPolicyNames.Upload)]
    [RequestSizeLimit(MaxWaybillImageBytes)]
    public async Task<IActionResult> ScanWaybill(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, $"{GuestSessionHeaderName} is required.");
        }

        if (file.Length == 0 || file.Length > MaxWaybillImageBytes)
        {
            throw new BadRequestException(ImageErrorCodes.InvalidImage, "Image must be JPG, PNG, or WEBP and no larger than 8 MB.");
        }

        await using Stream stream = file.OpenReadStream();
        return Ok(await deliveryRequestService.ScanWaybillAsync(
            id, token, stream, file.FileName, file.ContentType, cancellationToken));
    }

    [HttpPost("{id:guid}:resolve-recipient")]
    [AllowAnonymous]
    public async Task<IActionResult> ResolveRecipient(
        Guid id,
        [FromBody] ResolveRecipientRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, $"{GuestSessionHeaderName} is required.");
        }

        return Ok(await deliveryRequestService.ResolveRecipientAsync(id, token, request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetGuestStatus(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, $"{GuestSessionHeaderName} is required.");
        }

        return Ok(await deliveryRequestService.GetAsync(id, token, cancellationToken));
    }

    // ==========================================
    // Issue #20: Resident Approval Flow Endpoints
    // ==========================================

    [HttpPost("{id:guid}:cancel")]
    [AllowAnonymous]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetGuestSessionToken(out string token))
        {
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, $"{GuestSessionHeaderName} is required.");
        }

        return Ok(await deliveryRequestService.CancelAsync(id, token, cancellationToken));
    }

    [HttpGet("{id:guid}/resident")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> GetResidentStatus(Guid id, CancellationToken cancellationToken) =>
        Ok(await deliveryRequestService.GetForResidentAsync(User.GetRequiredUserId(), id, cancellationToken));

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
            throw BadRequestException.For("status", "Only status=pending is supported.");
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
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, $"{GuestSessionHeaderName} is required.");
        }

        return Ok(await deliveryRequestService.OpenCompartmentAsync(id, token, cancellationToken));
    }

    private bool TryGetGuestSessionToken(out string token)
    {
        token = Request.Headers[GuestSessionHeaderName].FirstOrDefault()?.Trim() ?? string.Empty;
        return token.Length > 0;
    }
}
