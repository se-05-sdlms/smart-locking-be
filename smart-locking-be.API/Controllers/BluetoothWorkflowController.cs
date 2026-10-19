using System.Security.Claims;
using System.Security.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

/// <summary>
/// Đầu vào HTTP cho workflow Bluetooth. Controller lấy danh tính từ JWT và chuyển request
/// sang Application; service quyết định quyền/luồng, không tin userId do client tự gửi.
/// </summary>
[ApiController, Route("api/bluetooth/workflow")]
[EnableRateLimiting(RateLimitPolicyNames.DeviceCommand)]
public sealed class BluetoothWorkflowController(IBluetoothWorkflowService service) : ControllerBase
{
    /// <summary>
    /// Chuẩn bị lệnh để client chuyển xuống tủ. Cho phép anonymous để hỗ trợ guest,
    /// nhưng D/G vẫn cần session hợp lệ; P/R/N cần JWT resident qua kiểm tra tại service.
    /// Trả lệnh thành công không có nghĩa cửa đã mở hoặc hàng đã được đặt/lấy xong.
    /// </summary>
    [HttpPost("prepare"), AllowAnonymous]
    public Task<BluetoothCommandResponse> Prepare(BluetoothPrepareRequest request, CancellationToken ct) =>
        service.PrepareAsync(User.IsInRole("Resident") && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null, request, ct);

    /// <summary>Cấp permit cư dân có hạn để chuẩn bị thao tác offline khi chính sách đang bật.</summary>
    [HttpGet("resident-permit"), Authorize(Roles = "Resident")]
    public Task<ResidentBluetoothPermitResponse> Permit(CancellationToken ct) => service.GetPermitAsync(UserId(), ct);

    /// <summary>
    /// Gắn URL ảnh đã upload cho yêu cầu của chính resident, hỗ trợ bổ sung bằng chứng sau khi có mạng.
    /// Không thay đổi trạng thái hàng, không gửi lệnh xuống tủ; trả 204 khi lưu thành công.
    /// </summary>
    [HttpPut("returns/{id:guid}/image"), Authorize(Roles = "Resident")]
    public async Task<IActionResult> Image(Guid id, OfflineReturnImageRequest request, CancellationToken ct)
    {
        await service.AttachImageAsync(UserId(), id, request.ImageUrl, ct);
        return NoContent();
    }

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new AuthenticationException("Token thiếu thông tin định danh hợp lệ.");
}
