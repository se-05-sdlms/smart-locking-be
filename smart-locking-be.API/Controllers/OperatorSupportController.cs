using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api")]
public sealed class OperatorSupportController(IOperatorSupportService service) : ControllerBase
{
    [HttpGet("lockers/public/by-code/{code}/support"), AllowAnonymous, EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
    public async Task<IActionResult> Public(string code, CancellationToken ct) => Ok(await service.GetPublicAsync(code, ct));

    [HttpGet("lockers/{id:guid}/support"), Authorize(Roles = "Administrator,LockerOperator,Resident")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await service.GetForUserAsync(userId, role, id, ct));
    }

    [HttpGet("operators/{id:guid}/support-contact"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Contact(Guid id, CancellationToken ct) => Ok(await service.GetContactAsync(id, ct));

    [HttpPut("operators/{id:guid}/support-contact"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SaveContact(Guid id, SaveOperatorSupportContactRequest request, CancellationToken ct) =>
        Ok(await service.SaveContactAsync(User.GetRequiredUserId(), id, request, ct));

    [HttpGet("lockers/{id:guid}/support-settings"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Settings(Guid id, CancellationToken ct) => Ok(await service.GetSettingsAsync(id, ct));

    [HttpPut("lockers/{id:guid}/support-settings"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SaveSettings(Guid id, SaveLockerSupportSettingsRequest request, CancellationToken ct) =>
        Ok(await service.SaveSettingsAsync(User.GetRequiredUserId(), id, request, ct));
}
