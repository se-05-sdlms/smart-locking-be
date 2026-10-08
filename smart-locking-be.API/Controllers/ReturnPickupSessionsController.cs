using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/return-pickup-sessions"), AllowAnonymous]
public sealed class ReturnPickupSessionsController(IReturnPickupSessionService service) : ControllerBase
{
    private const string GuestSessionHeaderName = "X-Guest-Session-Token";

    [HttpPost]
    public Task<IActionResult> Create(ValidateReturnPickupRequest request, CancellationToken ct) =>
        Execute(() => service.CreateAsync(request, ct), StatusCodes.Status201Created);

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        WithToken(token => service.GetAsync(id, token, ct));

    [HttpPost("{id:guid}:openCompartment")]
    public Task<IActionResult> OpenCompartment(Guid id, CancellationToken ct) =>
        WithToken(token => service.OpenCompartmentAsync(id, token, ct));

    private Task<IActionResult> WithToken<T>(Func<string, Task<T>> action)
    {
        string token = Request.Headers[GuestSessionHeaderName].FirstOrDefault()?.Trim() ?? string.Empty;
        return token.Length == 0
            ? Task.FromResult<IActionResult>(Unauthorized(new { message = $"Thiếu {GuestSessionHeaderName}." }))
            : Execute(() => action(token));
    }

    private static async Task<IActionResult> Execute<T>(Func<Task<T>> action, int statusCode = StatusCodes.Status200OK)
    {
        try { return new ObjectResult(await action()) { StatusCode = statusCode }; }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return new UnauthorizedObjectResult(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
        catch (TimeoutException ex) { return new ObjectResult(new { message = ex.Message }) { StatusCode = 410 }; }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
    }
}
