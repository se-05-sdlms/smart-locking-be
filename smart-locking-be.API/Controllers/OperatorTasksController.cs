using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Operations;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/operator-tasks"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class OperatorTasksController(IOperatorTaskService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int offset = 0, [FromQuery] int limit = 30, [FromQuery] Guid? lockerId = null)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await service.ListAsync(userId, role, offset, limit, lockerId, ct));
    }
    [HttpGet("{kind}/{id:guid}/handovers")]
    public async Task<IActionResult> History(string kind, Guid id, CancellationToken ct)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await service.HistoryAsync(userId, role, kind, id, ct));
    }
    [HttpPost("{kind}/{id:guid}/handovers")]
    public async Task<IActionResult> Create(string kind, Guid id, CreateTaskHandoverRequest request, CancellationToken ct)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await service.CreateHandoverAsync(userId, role, kind, id, request, ct));
    }
    [HttpPost("handovers/{id:guid}/acknowledgements")]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeTaskHandoverRequest request, CancellationToken ct)
    {
        var (userId, role) = User.GetRequiredIdentity();
        return Ok(await service.AcknowledgeAsync(userId, role, id, request, ct));
    }
}
