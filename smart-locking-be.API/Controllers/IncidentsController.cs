using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize(Roles = "Resident,LockerOperator,Administrator")]
public sealed class IncidentsController(IIncidentService incidentService) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Resident")]
    public async Task<IActionResult> Create(
        [FromBody] CreateIncidentRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async (userId, _) =>
        {
            IncidentDetailResponse response = await incidentService.CreateResidentIncidentAsync(
                userId,
                request,
                cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        });

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] IncidentStatus? status,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(async (userId, role) => Ok(
            role == nameof(UserRole.Resident)
                ? await incidentService.GetResidentIncidentsAsync(userId, cancellationToken, pageNumber, pageSize)
                : await incidentService.GetOperationalIncidentsAsync(userId, role, status, cancellationToken, pageNumber, pageSize)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        await ExecuteAsync(async (userId, role) =>
            Ok(await incidentService.GetIncidentAsync(userId, role, id, cancellationToken)));

    [HttpPost("{id:guid}/actions")]
    [Authorize(Roles = "LockerOperator,Administrator")]
    public async Task<IActionResult> AddAction(
        Guid id,
        [FromBody] AddIncidentActionRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async (userId, role) =>
            StatusCode(StatusCodes.Status201Created,
                await incidentService.AddActionAsync(userId, role, id, request, cancellationToken)));

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "LockerOperator,Administrator")]
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

        return await action(userId, User.FindFirstValue(ClaimTypes.Role)!);
    }
}
