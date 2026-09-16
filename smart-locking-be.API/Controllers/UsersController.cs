using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Users;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ApiPolicies.Administrator)]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    private Guid? GetCurrentUserId()
    {
        string? idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : null;
    }

    private string? GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet("residents")]
    [ProducesResponseType(typeof(PagedResult<ResidentListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetResidents(
        [FromQuery] GetUsersFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var result = await userService.GetResidentsAsync(filter, cancellationToken);
        return Ok(result);
    }

    [HttpGet("residents/{id:guid}")]
    [ProducesResponseType(typeof(ResidentDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetResidentById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await userService.GetResidentByIdAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("residents/{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateResidentStatus(
        Guid id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        if (adminId is null)
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        try
        {
            await userService.UpdateResidentStatusAsync(adminId.Value, id, request, GetIpAddress(), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("operators")]
    [ProducesResponseType(typeof(PagedResult<OperatorListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperators(
        [FromQuery] GetUsersFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var result = await userService.GetOperatorsAsync(filter, cancellationToken);
        return Ok(result);
    }

    [HttpGet("operators/{id:guid}")]
    [ProducesResponseType(typeof(OperatorDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperatorById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await userService.GetOperatorByIdAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("operators")]
    [ProducesResponseType(typeof(CreateOperatorResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateOperator(
        [FromBody] CreateOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        if (adminId is null)
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        try
        {
            var result = await userService.CreateOperatorAsync(adminId.Value, request, GetIpAddress(), cancellationToken);
            return CreatedAtAction(nameof(GetOperatorById), new { id = result.UserId }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("operators/{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateOperatorStatus(
        Guid id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        if (adminId is null)
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        try
        {
            await userService.UpdateOperatorStatusAsync(adminId.Value, id, request, GetIpAddress(), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("operators/{id:guid}/assignments")]
    [ProducesResponseType(typeof(OperatorAssignmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignOperatorScope(
        Guid id,
        [FromBody] AssignOperatorScopeRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        if (adminId is null)
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        try
        {
            var result = await userService.AssignOperatorScopeAsync(adminId.Value, id, request, GetIpAddress(), cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("operators/{id:guid}/assignments/{assignmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeOperatorScope(
        Guid id,
        Guid assignmentId,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        if (adminId is null)
        {
            return Unauthorized(new { message = "Không xác định được danh tính quản trị viên." });
        }

        try
        {
            await userService.RevokeOperatorScopeAsync(adminId.Value, id, assignmentId, reason, GetIpAddress(), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
