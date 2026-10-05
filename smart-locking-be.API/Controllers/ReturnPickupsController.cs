using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/return-pickups"), AllowAnonymous]
public sealed class ReturnPickupsController(IReturnRequestService service) : ControllerBase
{
    private const string Header = "X-Guest-Session-Token";
    [HttpPost("validate")] public Task<IActionResult> Validate(ValidateReturnPickupRequest request, CancellationToken ct) => Execute(() => service.ValidatePickupAsync(request, ct));
    [HttpPost("{id:guid}/open")] public Task<IActionResult> Open(Guid id, CancellationToken ct) => WithToken(token => service.OpenForPickupAsync(id, token, ct));
    [HttpPost("{id:guid}/confirm")] public Task<IActionResult> Confirm(Guid id, CancellationToken ct) => WithToken(token => service.ConfirmPickupAsync(id, token, ct));

    private Task<IActionResult> WithToken<T>(Func<string, Task<T>> action)
    {
        string token = Request.Headers[Header].FirstOrDefault()?.Trim() ?? string.Empty;
        return token.Length == 0 ? Task.FromResult<IActionResult>(Unauthorized(new { message = $"Thiếu {Header}." })) : Execute(() => action(token));
    }
    private static async Task<IActionResult> Execute<T>(Func<Task<T>> action)
    {
        try { return new OkObjectResult(await action()); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return new UnauthorizedObjectResult(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
        catch (TimeoutException ex) { return new ObjectResult(new { message = ex.Message }) { StatusCode = 410 }; }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new { message = ex.Message }); }
    }
}
