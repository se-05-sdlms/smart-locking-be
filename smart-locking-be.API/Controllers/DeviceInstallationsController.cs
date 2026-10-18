using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.DeviceInstallations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/device-installations")]
[Authorize(Policy = ApiPolicies.Resident)]
public sealed class DeviceInstallationsController(IDeviceInstallationService deviceInstallationService) : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceInstallationRequest request,
        CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        return Ok(await deviceInstallationService.RegisterAsync(userId, request, cancellationToken));
    }

    [HttpDelete("{installationId}")]
    public async Task<IActionResult> Deactivate(string installationId, CancellationToken cancellationToken)
    {
        Guid userId = User.GetRequiredUserId();

        await deviceInstallationService.DeactivateAsync(userId, installationId, cancellationToken);
        return NoContent();
    }
}
