using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/operator/incidents")]
[Authorize(Roles = "LockerOperator,Administrator")]
public sealed class OperatorIncidentsController(IIncidentService incidentService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] IncidentStatus? status,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async (userId, role) =>
            Ok(await incidentService.GetOperationalIncidentsAsync(
                userId,
                role,
                status,
                cancellationToken)));

    [HttpPost("{id:guid}/actions")]
    public async Task<IActionResult> AddAction(
        Guid id,
        [FromBody] AddIncidentActionRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async (userId, role) =>
            Ok(await incidentService.AddActionAsync(userId, role, id, request, cancellationToken)));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateIncidentStatusRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async (userId, role) =>
            Ok(await incidentService.UpdateStatusAsync(userId, role, id, request, cancellationToken)));

    private async Task<IActionResult> ExecuteAsync(Func<Guid, string, Task<IActionResult>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId) ||
            string.IsNullOrWhiteSpace(User.FindFirstValue(ClaimTypes.Role)))
        {
            return Unauthorized(new { message = "Invalid authentication token." });
        }

        try
        {
            return await action(userId, User.FindFirstValue(ClaimTypes.Role)!);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
