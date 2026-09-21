using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Administrator,LockerOperator")]
public class LockersController(ILockerService lockerService) : ControllerBase
{
    /// <summary>
    /// Lấy danh sách tủ Locker (Admin xem tất cả, LockerOperator xem danh sách được phân công).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLockers([FromQuery] string? search, CancellationToken cancellationToken)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => lockerService.GetLockersAsync(userId, userRole, search, cancellationToken));
    }

    /// <summary>
    /// Tạo mới một tủ Locker (Chỉ Administrator).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> CreateLocker(
        [FromBody] CreateLockerRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await lockerService.CreateLockerAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetLockerById), new { id = result.Id }, result);
        });
    }

    /// <summary>
    /// Lấy chi tiết tủ Locker theo ID (bao gồm các ngăn thuộc tủ).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetLockerById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => lockerService.GetLockerByIdAsync(userId, userRole, id, cancellationToken));
    }

    /// <summary>
    /// Cập nhật thông tin tủ Locker (Chỉ Administrator).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateLocker(
        Guid id,
        [FromBody] UpdateLockerRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => lockerService.UpdateLockerAsync(id, request, cancellationToken));
    }

    /// <summary>
    /// Vô hiệu hóa tủ Locker (Soft delete - chuyển OperationalStatus thành Inactive). Chỉ Admin mới được thực hiện.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SoftDeleteLocker(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            await lockerService.SoftDeleteLockerAsync(id, cancellationToken);
            return NoContent();
        });
    }

    /// <summary>
    /// Lấy danh sách các ngăn (Compartments) thuộc tủ Locker.
    /// </summary>
    [HttpGet("{id:guid}/compartments")]
    public async Task<IActionResult> GetCompartments(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => lockerService.GetCompartmentsAsync(userId, userRole, id, cancellationToken));
    }

    /// <summary>
    /// Tạo thêm ngăn (Compartment) cho tủ Locker (Chỉ Administrator).
    /// </summary>
    [HttpPost("{id:guid}/compartments")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> CreateCompartment(
        Guid id,
        [FromBody] CreateCompartmentRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await lockerService.CreateCompartmentAsync(id, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
        });
    }

    /// <summary>
    /// Cập nhật trạng thái vận hành của ngăn tủ (Locker Compartment). Administrator hoặc LockerOperator được phân công.
    /// </summary>
    [HttpPut("{id:guid}/compartments/{compartmentId:guid}/status")]
    [Authorize(Roles = "Administrator,LockerOperator")]
    public async Task<IActionResult> UpdateCompartmentStatus(
        Guid id,
        Guid compartmentId,
        [FromBody] UpdateCompartmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => lockerService.UpdateCompartmentStatusAsync(userId, userRole, id, compartmentId, request, cancellationToken));
    }

    private bool TryGetUserIdAndRole(out Guid userId, out string userRole)
    {
        userRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        string? claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private async Task<IActionResult> ExecuteAsync<TResponse>(Func<Task<TResponse>> action)
    {
        try
        {
            TResponse result = await action();
            if (result is IActionResult actionResult)
            {
                return actionResult;
            }

            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
