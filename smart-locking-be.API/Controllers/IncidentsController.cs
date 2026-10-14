using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Application.Interfaces.Services;
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
        CancellationToken cancellationToken)
    {
        var (userId, _) = GetIdentity();
        IncidentDetailResponse response = await incidentService.CreateResidentIncidentAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Resident")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken) =>
        Ok(await incidentService.GetResidentIncidentsAsync(GetIdentity().UserId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = GetIdentity();
        return Ok(await incidentService.GetIncidentAsync(userId, role, id, cancellationToken));
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

    [HttpPost("guest")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
    public async Task<IActionResult> CreateGuest(
        [FromBody] CreateGuestIncidentRequest request,
        CancellationToken cancellationToken)
    {
        GuestIncidentResponse response = await incidentService.CreateGuestIncidentAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
