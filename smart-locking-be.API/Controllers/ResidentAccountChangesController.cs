using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Authorization;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/resident-account-changes"), Authorize(Roles = "Resident,Administrator")]
public sealed class ResidentAccountChangesController(IResidentAccountChangeService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 30) =>
        Ok(await service.GetAsync(User.GetRequiredUserId(), pageNumber, pageSize, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(User.GetRequiredUserId(), id, cancellationToken));
    [HttpGet("locker-eligibility/{targetLockerId:guid}"), Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> Eligibility(Guid targetLockerId, CancellationToken cancellationToken) =>
        Ok(await service.EligibilityAsync(User.GetRequiredUserId(), targetLockerId, cancellationToken));
    [HttpPost("locker"), Authorize(Policy = ApiPolicies.Resident), EnableRateLimiting(RateLimitPolicyNames.HeavyAction)]
    public async Task<IActionResult> Locker(CreateLockerChangeRequest request, CancellationToken cancellationToken) =>
        Ok(await service.CreateLockerAsync(User.GetRequiredUserId(), request, cancellationToken));
    [HttpPost("phone:request-otp"), Authorize(Policy = ApiPolicies.Resident), EnableRateLimiting(RateLimitPolicyNames.Auth)]
    public async Task<IActionResult> PhoneOtp(RequestPhoneChangeOtpRequest request, CancellationToken cancellationToken)
    {
        await service.RequestPhoneOtpAsync(User.GetRequiredUserId(), request, cancellationToken);
        return NoContent();
    }
    [HttpPost("phone"), Authorize(Policy = ApiPolicies.Resident), EnableRateLimiting(RateLimitPolicyNames.Auth)]
    public async Task<IActionResult> Phone(CreatePhoneChangeRequest request, CancellationToken cancellationToken) =>
        Ok(await service.CreatePhoneAsync(User.GetRequiredUserId(), request, cancellationToken));
    [HttpPost("{id:guid}:review"), Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> Review(Guid id, ReviewAccountChangeRequest request, CancellationToken cancellationToken) =>
        Ok(await service.ReviewAsync(User.GetRequiredUserId(), id, request, cancellationToken));
    [HttpPost("{id:guid}:cancel"), Authorize(Policy = ApiPolicies.Resident)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(User.GetRequiredUserId(), id, cancellationToken));
}
