using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.Interfaces.Services;
using System.Security.Claims;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/parcels")]
[Authorize(Policy = ApiPolicies.LockerOperator)]
public sealed class ParcelsController(IOperatorService operatorService) : ControllerBase
{
    /// <summary>Danh sách bưu kiện quá hạn trong các tủ được phân công.</summary>
    [HttpGet]
    public async Task<IActionResult> GetOverdueParcels(
        [FromQuery] Guid? lockerId = null, [FromQuery] string? search = null,
        [FromQuery] string status = "overdue", [FromQuery] bool? threeDaysOrMore = null,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid operatorId))
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });

        if (!string.Equals(status, "overdue", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Chỉ hỗ trợ lọc bưu kiện quá hạn (status=overdue)." });

        try
        {
            return Ok(await operatorService.GetOverdueParcelsAsync(
                operatorId, lockerId, search, threeDaysOrMore, pageNumber, pageSize, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>Chi tiết một bưu kiện quá hạn trong phạm vi được phân công.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOverdueParcel(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid operatorId))
            return Unauthorized(new { message = "Token không hợp lệ hoặc thiếu thông tin định danh." });

        try
        {
            return Ok(await operatorService.GetOverdueParcelAsync(operatorId, id, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
