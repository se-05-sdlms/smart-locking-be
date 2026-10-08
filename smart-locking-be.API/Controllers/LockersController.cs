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
    [HttpGet("registration-options")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRegistrationOptions(CancellationToken cancellationToken) =>
        Ok(await lockerService.GetRegistrationOptionsAsync(cancellationToken));

    /// <summary>
    /// Lấy danh sách tủ Locker (Admin xem tất cả, LockerOperator xem danh sách được phân công).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLockers(
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => lockerService.GetLockersAsync(userId, userRole, search, cancellationToken, pageNumber, pageSize));
    }

    [HttpGet("operational-summary")]
    public async Task<IActionResult> GetOperationalSummary(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });

        return await ExecuteAsync(() => lockerService.GetOperationalSummaryAsync(
            userId, userRole, cancellationToken, pageNumber, pageSize));
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
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateLocker(
        Guid id,
        [FromBody] UpdateLockerRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => lockerService.UpdateLockerAsync(id, request, cancellationToken));
    }

    [HttpPatch("{id:guid}/operational-status")]
    public async Task<IActionResult> UpdateOperationalStatus(
        Guid id,
        [FromBody] UpdateOperationalStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() =>
            lockerService.UpdateOperationalStatusAsync(userId, userRole, id, request, cancellationToken));
    }

    /// <summary>
    /// Lấy danh sách các ngăn (Compartments) thuộc tủ Locker.
    /// </summary>
    [HttpGet("{id:guid}/compartments")]
    public async Task<IActionResult> GetCompartments(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => lockerService.GetCompartmentsAsync(
            userId, userRole, id, cancellationToken, pageNumber, pageSize));
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
    [HttpPatch("{id:guid}/compartments/{compartmentId:guid}/operational-status")]
    [Authorize(Roles = "Administrator,LockerOperator")]
    public async Task<IActionResult> UpdateCompartmentOperationalStatus(
        Guid id,
        Guid compartmentId,
        [FromBody] UpdateOperationalStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserIdAndRole(out Guid userId, out string userRole))
        {
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });
        }

        return await ExecuteAsync(() => lockerService.UpdateCompartmentOperationalStatusAsync(userId, userRole, id, compartmentId, request, cancellationToken));
    }

    private bool TryGetUserIdAndRole(out Guid userId, out string userRole)
    {
        userRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        string? claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private async Task<IActionResult> ExecuteAsync<TResponse>(Func<Task<TResponse>> action)
    {
        TResponse result = await action();
        return result is IActionResult actionResult ? actionResult : Ok(result);
    }
}
