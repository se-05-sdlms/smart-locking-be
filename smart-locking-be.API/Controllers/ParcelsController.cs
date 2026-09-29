using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Parcels;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/parcels")]
[Authorize(Roles = "Resident,LockerOperator")]
public sealed class ParcelsController(IParcelService parcelService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetParcels(
        [FromQuery] ParcelListView view = ParcelListView.Active,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetParcelsAsync(userId, role, view, search, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetParcel(Guid id, CancellationToken cancellationToken) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetParcelAsync(userId, role, id, cancellationToken));

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken) =>
        await ExecuteAsync((userId, role) =>
            parcelService.GetHistoryAsync(userId, role, id, cancellationToken));

    private async Task<IActionResult> ExecuteAsync<TResponse>(
        Func<Guid, string, Task<TResponse>> action)
    {
        string? idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? role = User.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(idClaim, out Guid userId) || string.IsNullOrWhiteSpace(role))
        {
            return Unauthorized(new { message = "Invalid authentication token." });
        }

        try
        {
            return Ok(await action(userId, role));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }
}
