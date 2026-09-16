using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ApiPolicies.Administrator)]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] GetUsersFilterRequest filter,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => userService.GetUsersAsync(filter, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUserById(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => userService.GetUserByIdAsync(id, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid adminId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        return await ExecuteAsync(() => userService.CreateUserAsync(adminId, request, GetIpAddress(), cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid adminId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        return await ExecuteAsync(() => userService.UpdateUserAsync(adminId, id, request, GetIpAddress(), cancellationToken));
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(
        Guid id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid adminId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        return await ExecuteAsync(() => userService.UpdateUserStatusAsync(adminId, id, request, GetIpAddress(), cancellationToken));
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> AssignOperatorScope(
        Guid id,
        [FromBody] AssignOperatorScopeRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid adminId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        return await ExecuteAsync(() => userService.AssignOperatorScopeAsync(adminId, id, request, GetIpAddress(), cancellationToken));
    }

    [HttpDelete("{id:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> RevokeOperatorScope(
        Guid id,
        Guid assignmentId,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out Guid adminId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        try
        {
            await userService.RevokeOperatorScopeAsync(adminId, id, assignmentId, reason, GetIpAddress(), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        string? claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private string? GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<IActionResult> ExecuteAsync<TResponse>(Func<Task<TResponse>> action)
    {
        try
        {
            TResponse result = await action();
            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.Contains("đã được sử dụng", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { message = exception.Message });
            }

            return BadRequest(new { message = exception.Message });
        }
    }
}
