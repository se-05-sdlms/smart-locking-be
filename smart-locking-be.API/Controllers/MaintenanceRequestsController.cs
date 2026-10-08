using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/maintenance-requests"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class MaintenanceRequestsController(IOperationsService service) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => Execute((userId, role) => service.GetMaintenanceAsync(userId, role, ct));

    [HttpPost]
    public Task<IActionResult> Create(CreateMaintenanceRequest request, CancellationToken ct) =>
        Execute((userId, role) => service.CreateMaintenanceAsync(userId, role, request, ct), StatusCodes.Status201Created);

    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateMaintenanceRequest request, CancellationToken ct) =>
        Execute((userId, role) => service.UpdateMaintenanceAsync(userId, role, id, request, ct));

    private async Task<IActionResult> Execute<T>(Func<Guid, string, Task<T>> action, int statusCode = StatusCodes.Status200OK)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)) return Unauthorized();
        string role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        try { return StatusCode(statusCode, await action(userId, role)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
