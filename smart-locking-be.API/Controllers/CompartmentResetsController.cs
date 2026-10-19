using System.Security.Claims;
using System.Security.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

/// <summary>
/// Cho phép quản trị viên hoặc nhân viên vận hành đặt lại trạng thái ngăn bị kẹt sau khi đã lấy hết hàng.
/// Service kiểm tra phạm vi quyền: Administrator được xử lý mọi tủ,
/// LockerOperator chỉ được xử lý tủ đang được phân công. Resident và guest không được truy cập.
/// BE chỉ hoàn tất đặt lại khi nhận phản hồi ESP khớp yêu cầu; lệnh đặt lại không kích relay mở chốt.
/// </summary>
[ApiController]
[Authorize(Roles = "Administrator,LockerOperator")]
[Route("api/compartment-resets")]
public sealed class CompartmentResetsController(ICompartmentResetService service) : ControllerBase
{
    // Lấy ID người thực hiện từ JWT đã được xác thực để kiểm tra quyền và ghi nhật ký thao tác.
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
        ? actor
        : throw new AuthenticationException("Token thiếu thông tin định danh hợp lệ.");

    // Truyền vai trò trong JWT xuống service để kiểm tra quyền trên đúng tủ được yêu cầu.
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "";

    /// <summary>
    /// Lấy thiết bị, kênh phần cứng, mã giao dịch gợi ý và yêu cầu đặt lại đang chờ của ngăn.
    /// Dùng để hiển thị thông tin cho người vận hành đối chiếu trước khi gửi lệnh.
    /// Thao tác này chỉ đọc dữ liệu, không đặt lại ngăn hoặc mở chốt.
    /// </summary>
    [HttpGet("lockers/{lockerId:guid}/compartments/{compartmentId:guid}")]
    public async Task<IActionResult> Context(Guid lockerId, Guid compartmentId, CancellationToken ct)
        => Ok(await service.ContextAsync(Actor, Role, lockerId, compartmentId, ct));

    /// <summary>
    /// Tạo hoặc thử lại yêu cầu đặt lại cho đúng ngăn và giao dịch.
    /// Người thực hiện phải xác nhận đã lấy hết hàng và cung cấp lý do.
    /// Service tạm ngừng cấp ngăn, gửi lệnh qua MQTT hoặc trả lệnh để client chuyển qua BLE.
    /// HTTP 200 chỉ cho biết yêu cầu đã được xử lý; cần xem trạng thái Pending/Applied trong response.
    /// </summary>
    [HttpPost("lockers/{lockerId:guid}/compartments/{compartmentId:guid}")]
    public async Task<IActionResult> Start(Guid lockerId, Guid compartmentId, CompartmentResetRequest request, CancellationToken ct)
        => Ok(await service.RequestAsync(Actor, Role, lockerId, compartmentId, request, ct));

    /// <summary>
    /// Kiểm tra kết quả của yêu cầu theo mã lệnh, ví dụ sau khi gửi qua MQTT.
    /// Pending nghĩa là chưa hoàn tất; Applied nghĩa là BE đã tiếp nhận phản hồi OK khớp yêu cầu.
    /// API không tự gửi lại lệnh xuống ESP.
    /// </summary>
    [HttpGet("{commandId:guid}")]
    public async Task<IActionResult> Status(Guid commandId, CancellationToken ct)
        => Ok(await service.GetAsync(Actor, Role, commandId, ct));

    /// <summary>
    /// Tiếp nhận chuỗi phản hồi đặt lại do client đọc từ ESP qua Bluetooth.
    /// Service đối chiếu mã lệnh, thiết bị, kênh và giao dịch trước khi cập nhật nghiệp vụ.
    /// Phản hồi lỗi không giải phóng ngăn; phản hồi đã áp dụng được xử lý lặp mà không cập nhật lần hai.
    /// Trạng thái cửa lấy từ phản hồi ESP, không mặc định cửa đã đóng sau khi đặt lại.
    /// </summary>
    [HttpPost("{commandId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid commandId, CompartmentResetConfirmation request, CancellationToken ct)
        => Ok(await service.ConfirmAsync(Actor, Role, commandId, request.Proof, ct));

    // ApiExceptionHandler xử lý lỗi tập trung: 401 token không hợp lệ, 403 sai phạm vi quyền,
    // 404 không tìm thấy dữ liệu, 400 đầu vào không hợp lệ, 409 xung đột trạng thái.
}
