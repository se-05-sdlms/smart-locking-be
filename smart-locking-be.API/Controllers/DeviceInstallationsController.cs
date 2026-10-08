using System.Security.Claims;
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
    [HttpPut("{installationId}")]
    public async Task<IActionResult> Register(
        string installationId,
        [FromBody] RegisterDeviceInstallationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => deviceInstallationService.RegisterAsync(userId, installationId, request, cancellationToken));
    }

    [HttpDelete("{installationId}")]
    public async Task<IActionResult> Deactivate(string installationId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        await deviceInstallationService.DeactivateAsync(userId, installationId, cancellationToken);
        return NoContent();
    }

    private bool TryGetUserId(out Guid userId)
    {
        string? claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private static async Task<IActionResult> ExecuteAsync<TResponse>(Func<Task<TResponse>> action) =>
        new OkObjectResult(await action());
}
