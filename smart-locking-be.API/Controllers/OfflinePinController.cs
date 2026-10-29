using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.API.Extensions;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

/// <summary>Cung cấp danh sách đối soát; chưa cho phép client HTTP chuyển tiếp nhật ký ESP chưa có chữ ký.</summary>
[ApiController, Route("api/bluetooth/offline")]
[EnableRateLimiting(RateLimitPolicyNames.DeviceCommand)]
public sealed class OfflinePinController(IOfflinePinService service) : ControllerBase
{
    /// <summary>
    /// Route tương thích hiện từ chối toàn bộ proof unsigned, không lưu event hoặc trả ACK.
    /// Đăng nhập không chứng minh nguồn ESP. Chỉ mở lại HTTP relay khi có MAC/chữ ký thiết bị.
    /// </summary>
    [HttpPost("sync"), Authorize, RequestSizeLimit(65_000)]
    public async Task<OfflinePinSyncResponse> Sync(OfflinePinSyncRequest request, CancellationToken ct) =>
        await service.SyncAsync(request, ct);
    /// <summary>Tra cứu kết quả MQTT của chính cư dân; không nhận hay áp dụng proof HTTP.</summary>
    [HttpPost("reconcile"), Authorize(Roles = "Resident"), RequestSizeLimit(10_000)]
    public Task<OfflinePinSyncResponse> Reconcile(OfflinePinReconcileRequest request, CancellationToken ct) =>
        service.ReconcileAsync(User.GetRequiredUserId(), request, ct);
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
