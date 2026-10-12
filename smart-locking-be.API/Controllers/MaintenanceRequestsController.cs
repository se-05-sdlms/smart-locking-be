using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/maintenance-requests"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class MaintenanceRequestsController(IMaintenanceService service) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(
        [FromQuery] Guid? lockerId, [FromQuery] Guid? compartmentId,
        [FromQuery] MaintenanceStatus? status, [FromQuery] MaintenancePriority? priority,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Execute((userId, role) => service.GetAsync(
            userId, role, lockerId, compartmentId, status, priority, from, to, ct, pageNumber, pageSize));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Execute((userId, role) => service.GetByIdAsync(userId, role, id, ct));

    [HttpPost]
    public Task<IActionResult> Create(CreateMaintenanceRequest request, CancellationToken ct) =>
        Execute((userId, role) => service.CreateAsync(userId, role, request, ct), StatusCodes.Status201Created);

    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateMaintenanceRequest request, CancellationToken ct) =>
        Execute((userId, role) => service.UpdateAsync(userId, role, id, request, ct));

    private async Task<IActionResult> Execute<T>(Func<Guid, string, Task<T>> action, int statusCode = StatusCodes.Status200OK)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)) return Unauthorized();
        string role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return StatusCode(statusCode, await action(userId, role));
    }
}
