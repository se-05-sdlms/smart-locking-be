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
        CancellationToken cancellationToken)
    {
        var (userId, role) = GetIdentity();
        return Ok(await incidentService.GetOperationalIncidentsAsync(userId, role, status, cancellationToken));
    }

    [HttpPost("{id:guid}/actions")]
    public async Task<IActionResult> AddAction(
        Guid id,
        [FromBody] AddIncidentActionRequest request,
        CancellationToken cancellationToken)
    {
        var (userId, role) = GetIdentity();
        return Ok(await incidentService.AddActionAsync(userId, role, id, request, cancellationToken));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateIncidentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var (userId, role) = GetIdentity();
        return Ok(await incidentService.UpdateStatusAsync(userId, role, id, request, cancellationToken));
    }

    private (Guid UserId, string Role) GetIdentity()
    {
        string? role = User.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId) ||
            string.IsNullOrWhiteSpace(role))
        {
            throw new UnauthorizedException("Invalid authentication token.");
        }

        return (userId, role);
    }
}
