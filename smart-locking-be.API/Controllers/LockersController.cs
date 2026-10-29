using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using System.Text.Json;
using smart_locking_be.API.Services;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Administrator,LockerOperator")]
public class LockersController(ILockerService lockerService) : ControllerBase
{
    /// <summary>Thông tin locker tối thiểu cho Guest Web từ mã trên QR.</summary>
    [HttpGet("public/by-code/{code}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
    public async Task<IActionResult> GetPublicLocker(string code, CancellationToken cancellationToken) =>
        Ok(await lockerService.GetPublicLockerAsync(code, cancellationToken));

    /// <summary>Trạng thái BE nhận qua MQTT, có thời điểm quan sát; không phát lệnh xuống tủ.</summary>
    [HttpGet("public/by-code/{code}/status"), AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
    public async Task<IActionResult> GetPublicStatus(string code, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await lockerService.GetPublicLockerStatusAsync(code, cancellationToken));
    }

    /// <summary>Đẩy snapshot ngay khi BE đã lưu thay đổi MQTT. Keepalive chỉ giữ kết nối, không polling DB.</summary>
    [HttpGet("public/by-code/{code}/status/events"), AllowAnonymous, DisableRequestTimeout]
    [EnableRateLimiting(RateLimitPolicyNames.PublicApi)]
    public async Task StreamPublicStatus(string code, [FromServices] LockerStatusStream stream, CancellationToken ct)
    {
        var first = await lockerService.GetPublicLockerStatusAsync(code, ct);
        using var subscription = stream.Subscribe(first.DeviceIdentifier);
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        async Task WriteSnapshot()
        {
            var snapshot = await lockerService.GetPublicLockerStatusAsync(code, ct);
            await Response.WriteAsync("event: status\ndata: " + JsonSerializer.Serialize(snapshot, json) + "\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        try
        {
            // Subscribe trước lần đọc đầu để không bỏ mất thay đổi giữa lúc đọc và đăng ký.
            await WriteSnapshot();
            while (!ct.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                try { await subscription.WaitAsync(heartbeat.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await Response.WriteAsync(": keepalive\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }
                await WriteSnapshot();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>
    /// Lấy danh sách tủ Locker (Admin xem tất cả, LockerOperator xem danh sách được phân công).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLockers([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var (userId, userRole) = User.GetRequiredIdentity();

        return Ok(await lockerService.GetLockersAsync(userId, userRole, search, cancellationToken));
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
        var result = await lockerService.CreateLockerAsync(User.GetRequiredUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetLockerById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Lấy chi tiết tủ Locker theo ID (bao gồm các ngăn thuộc tủ).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetLockerById(Guid id, CancellationToken cancellationToken)
    {
        var (userId, userRole) = User.GetRequiredIdentity();

        return Ok(await lockerService.GetLockerByIdAsync(userId, userRole, id, cancellationToken));
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
        return Ok(await lockerService.UpdateLockerAsync(User.GetRequiredUserId(), id, request, cancellationToken));
    }

    /// <summary>Operator được phân công tạm ngừng hoặc khôi phục locker, kèm lý do.</summary>
    [HttpPut("{id:guid}/status")]
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateLockerStatus(Guid id, [FromBody] UpdateLockerStatusRequest request,
        CancellationToken cancellationToken)
    {
        var (userId, userRole) = User.GetRequiredIdentity();
        return Ok(await lockerService.UpdateLockerStatusAsync(userId, userRole, id, request, cancellationToken));
    }

    /// <summary>
    /// Vô hiệu hóa tủ Locker (Soft delete - chuyển OperationalStatus thành Inactive). Chỉ Admin mới được thực hiện.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SoftDeleteLocker(Guid id, CancellationToken cancellationToken)
    {
        await lockerService.SoftDeleteLockerAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Lấy danh sách các ngăn (Compartments) thuộc tủ Locker.
    /// </summary>
    [HttpGet("{id:guid}/compartments")]
    public async Task<IActionResult> GetCompartments(Guid id, CancellationToken cancellationToken)
    {
        var (userId, userRole) = User.GetRequiredIdentity();

        return Ok(await lockerService.GetCompartmentsAsync(userId, userRole, id, cancellationToken));
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
        var result = await lockerService.CreateCompartmentAsync(User.GetRequiredUserId(), id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}/compartments/{compartmentId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateCompartment(Guid id, Guid compartmentId,
        [FromBody] UpdateCompartmentRequest request, CancellationToken cancellationToken) =>
        Ok(await lockerService.UpdateCompartmentAsync(User.GetRequiredUserId(), id, compartmentId, request, cancellationToken));

    [HttpDelete("{id:guid}/compartments/{compartmentId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> DeactivateCompartment(Guid id, Guid compartmentId, CancellationToken cancellationToken)
    {
        await lockerService.UpdateCompartmentStatusAsync(User.GetRequiredUserId(), "Administrator", id, compartmentId,
            new UpdateCompartmentStatusRequest(smart_locking_be.Domain.Enums.LockerCompartmentOperationalStatus.Inactive), cancellationToken);
        return NoContent();
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
        var (userId, userRole) = User.GetRequiredIdentity();

        return Ok(await lockerService.UpdateCompartmentStatusAsync(userId, userRole, id, compartmentId, request, cancellationToken));
    }
}
