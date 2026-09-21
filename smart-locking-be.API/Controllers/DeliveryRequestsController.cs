using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/delivery-requests")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
public sealed class DeliveryRequestsController(IDeliveryRequestService deliveryRequestService) : ControllerBase
{
    private const string GuestSessionHeaderName = "X-Guest-Session-Token";

    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiateDeliveryRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            async () => StatusCode(
                StatusCodes.Status201Created,
                await deliveryRequestService.InitiateAsync(request, cancellationToken)));

    [HttpPost("{id:guid}/upload-image")]
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

    private bool TryGetGuestSessionToken(out string token)
    {
        token = Request.Headers[GuestSessionHeaderName].FirstOrDefault()?.Trim() ?? string.Empty;
        return token.Length > 0;
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
