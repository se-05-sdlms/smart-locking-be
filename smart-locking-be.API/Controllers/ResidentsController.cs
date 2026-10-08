using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ApiPolicies.Resident)]
public class ResidentsController(IResidentService residentService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => residentService.GetProfileAsync(userId, cancellationToken));
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateResidentProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => residentService.UpdateProfileAsync(userId, request, cancellationToken));
    }

    private bool TryGetUserId(out Guid userId)
    {
        string? claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private async Task<IActionResult> ExecuteAsync<TResponse>(Func<Task<TResponse>> action)
        => Ok(await action());
}
