using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.SystemPolicies;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/system-policies"), Authorize(Roles = "Administrator")]
public sealed class SystemPoliciesController(ISystemPolicyService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool? isActive, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await service.GetAsync(isActive, pageNumber, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) => Ok(await service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(SaveSystemPolicyRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out Guid userId)) return Unauthorized();
        return StatusCode(StatusCodes.Status201Created, await service.CreateAsync(userId, request, ct));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveSystemPolicyRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out Guid userId)) return Unauthorized();
        return Ok(await service.UpdateAsync(userId, id, request, ct));
    }

    [HttpPost("{id:guid}:activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out Guid userId)) return Unauthorized();
        return Ok(await service.ActivateAsync(userId, id, ct));
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
