using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/maintenance-requests"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class MaintenanceRequestsController(IMaintenanceService service) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Execute((userId, role) => service.GetAsync(userId, role, ct, pageNumber, pageSize));

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
