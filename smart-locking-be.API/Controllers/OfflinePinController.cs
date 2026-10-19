using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

/// <summary>Nhận nhật ký offline do client chuyển tiếp và cung cấp danh sách đối soát cho người vận hành.</summary>
[ApiController, Route("api/bluetooth/offline")]
[EnableRateLimiting(RateLimitPolicyNames.DeviceCommand)]
public sealed class OfflinePinController(IOfflinePinService service) : ControllerBase
{
    /// <summary>
    /// Nhận lô nhật ký mở/đóng cửa từ ESP khi client có mạng trở lại. Service đối chiếu giao dịch,
    /// thiết bị, thứ tự event và chống áp dụng trùng; không suy ra hoàn tất chỉ từ lệnh mở chốt.
    /// Anonymous phục vụ chuyển tiếp offline; request được giới hạn kích thước và tần suất.
    /// </summary>
    [HttpPost("sync"), AllowAnonymous, RequestSizeLimit(65_000)]
    public async Task<OfflinePinSyncResponse> Sync(OfflinePinSyncRequest request, CancellationToken ct) =>
        await service.SyncAsync(request, ct);
    /// <summary>
    /// Tra cứu event phục vụ đối soát; Operator chỉ xem các tủ đang được phân công.
    /// Response không chứa PIN hoặc chuỗi nhật ký gốc chứa PIN.
    /// </summary>
    [HttpGet("events"), Authorize(Roles = "Administrator,LockerOperator")]
    public async Task<IActionResult> Events([FromQuery] string? device, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)) return Unauthorized();
        return Ok(await service.GetEventsAsync(userId, User.FindFirstValue(ClaimTypes.Role) ?? "", device, status, page, pageSize, ct));
    }
}
