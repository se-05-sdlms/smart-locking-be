using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/operator-absences"), Authorize(Roles = "Administrator,LockerOperator")]
public sealed class OperatorAbsencesController(IOperatorCoverageService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (id, role) = User.GetRequiredIdentity();
        return Ok(await service.ListAsync(id, role, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOperatorAbsenceRequest request, CancellationToken ct)
    {
        var (id, role) = User.GetRequiredIdentity();
        return StatusCode(201, await service.CreateAsync(id, role, request, ct));
    }

    [HttpPost("{id:guid}/proposals"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Suggest(Guid id, CancellationToken ct) =>
        Ok(await service.SuggestAsync(User.GetRequiredUserId(), id, ct));

    [HttpPost("proposals/{id:guid}/approve"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Approve(Guid id, ReviewOperatorReplacementRequest request, CancellationToken ct) =>
        Ok(await service.ApproveAsync(User.GetRequiredUserId(), id, request, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelOperatorAbsenceRequest request, CancellationToken ct)
    {
        var (actorId, role) = User.GetRequiredIdentity();
        return Ok(await service.CancelAsync(actorId, role, id, request.Reason, ct));
    }
}
public sealed record CancelOperatorAbsenceRequest(string Reason);
