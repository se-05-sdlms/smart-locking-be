using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ApiPolicies.Resident)]
public class ResidentsController(IResidentService residentService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        return Ok(await residentService.GetProfileAsync(userId, cancellationToken));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateResidentProfileRequest request,
        CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        return Ok(await residentService.UpdateProfileAsync(userId, request, cancellationToken));
    }

    [HttpPut("me/approval-mode")]
    public async Task<IActionResult> UpdateApprovalMode(
        [FromBody] UpdateApprovalModeRequest request,
        CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        return Ok(await residentService.UpdateApprovalModeAsync(userId, request, cancellationToken));
    }
}
