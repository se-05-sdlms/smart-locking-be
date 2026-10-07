using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/returns"), Authorize(Roles = "Resident")]
public sealed class ReturnsController(IReturnRequestService service) : ControllerBase
{
    [HttpPost] public Task<IActionResult> Create(CreateReturnRequest request, CancellationToken ct) => Execute(user => service.CreateAsync(user, request, ct));
    [HttpGet] public Task<IActionResult> Mine(CancellationToken ct) => Execute(user => service.GetMineAsync(user, ct));
    [HttpGet("{id:guid}")] public Task<IActionResult> Get(Guid id, CancellationToken ct) => Execute(user => service.GetAsync(user, id, ct));
    [HttpPost("{id:guid}:openCompartment")] public Task<IActionResult> OpenCompartment(Guid id, CancellationToken ct) => Execute(user => service.OpenCompartmentAsync(user, id, ct));

    private async Task<IActionResult> Execute<T>(Func<Guid, Task<T>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)) return Unauthorized();
        try { return Ok(await action(userId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (TimeoutException ex) { return StatusCode(410, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
