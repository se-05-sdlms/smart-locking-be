using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/admin/lockers/{lockerId:guid}/commissioning")]
[Authorize(Roles = "Administrator")]
public sealed class LockerCommissioningController(ILockerCommissioningService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid lockerId, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(lockerId, cancellationToken));

    [HttpPost("bind-device")]
    public async Task<IActionResult> Bind(Guid lockerId, BindCommissioningDeviceRequest request, CancellationToken cancellationToken) =>
        Ok(await service.BindAsync(User.GetRequiredUserId(), lockerId, request, cancellationToken));

    [HttpPost("configure")]
    public async Task<IActionResult> Configure(Guid lockerId, ConfigureCommissioningRequest request, CancellationToken cancellationToken) =>
        Ok(await service.ConfigureAsync(User.GetRequiredUserId(), lockerId, request, cancellationToken));

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(Guid lockerId, VerifyCommissioningRequest request, CancellationToken cancellationToken) =>
        Ok(await service.VerifyAsync(User.GetRequiredUserId(), lockerId, request, cancellationToken));

    [HttpPost("handover")]
    public async Task<IActionResult> Handover(Guid lockerId, HandoverCommissioningRequest request, CancellationToken cancellationToken) =>
        Ok(await service.HandoverAsync(User.GetRequiredUserId(), lockerId, request, cancellationToken));
}
