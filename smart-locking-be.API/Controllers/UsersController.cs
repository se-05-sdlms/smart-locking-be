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
        return Ok(await userService.GetUsersAsync(filter, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUserById(
        Guid id,
        CancellationToken cancellationToken)
    {
        return Ok(await userService.GetUserByIdAsync(id, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        Guid adminId = User.GetRequiredUserId();

        return Ok(await userService.CreateUserAsync(adminId, request, GetIpAddress(), cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        Guid adminId = User.GetRequiredUserId();

        return Ok(await userService.UpdateUserAsync(adminId, id, request, GetIpAddress(), cancellationToken));
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(
        Guid id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        Guid adminId = User.GetRequiredUserId();

        return Ok(await userService.UpdateUserStatusAsync(adminId, id, request, GetIpAddress(), cancellationToken));
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> AssignOperatorScope(
        Guid id,
        [FromBody] AssignOperatorScopeRequest request,
        CancellationToken cancellationToken)
    {
        Guid adminId = User.GetRequiredUserId();

        return Ok(await userService.AssignOperatorScopeAsync(adminId, id, request, GetIpAddress(), cancellationToken));
    }

    [HttpDelete("{id:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> RevokeOperatorScope(
        Guid id,
        Guid assignmentId,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        Guid adminId = User.GetRequiredUserId();

        await userService.RevokeOperatorScopeAsync(adminId, id, assignmentId, reason, GetIpAddress(), cancellationToken);
        return NoContent();
    }


    private string? GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}
