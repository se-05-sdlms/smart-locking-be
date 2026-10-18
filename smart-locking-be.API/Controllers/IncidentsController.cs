using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Application.Interfaces.Services;

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
        Guid userId = User.GetRequiredUserId();
        IncidentDetailResponse response = await incidentService.CreateResidentIncidentAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Resident")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken) =>
        Ok(await incidentService.GetResidentIncidentsAsync(User.GetRequiredUserId(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await incidentService.GetIncidentAsync(userId, role, id, cancellationToken));
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
