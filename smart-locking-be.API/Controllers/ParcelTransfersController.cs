using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/parcels"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class ParcelTransfersController(IOperationsService service) : ControllerBase
{
    [HttpPost("{parcelId:guid}:transferToCollectionPoint")]
    public async Task<IActionResult> Transfer(Guid parcelId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)) return Unauthorized();
        string role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        try { return Ok(await service.TransferOverdueAsync(userId, role, parcelId, ct)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
