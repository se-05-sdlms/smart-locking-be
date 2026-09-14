using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Authorization;
using smart_locking_be.Application.Interfaces;

namespace smart_locking_be.API.Controllers;

/// <summary>
/// Controller mẫu hướng dẫn tổ chức API, tiêm Service và áp dụng phân quyền với ApiPolicies.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SampleController : ControllerBase
{
    private readonly IService _service;

    public SampleController(IService service)
    {
        _service = service;
    }

    /// <summary>
    /// API Công khai (Public) không yêu cầu Token đăng nhập.
    /// GET api/sample/welcome
    /// </summary>
    [HttpGet("welcome")]
    [AllowAnonymous]
    public async Task<IActionResult> GetWelcome(CancellationToken cancellationToken)
    {
        string message = await _service.GetWelcomeMessageAsync(cancellationToken);

        return Ok(new { message });
    }

    /// <summary>
    /// API Bảo mật yêu cầu Quyền Quản trị viên (Administrator).
    /// GET api/sample/admin-only
    /// </summary>
    [HttpGet("admin-only")]
    [Authorize(Policy = ApiPolicies.Administrator)]
    public IActionResult GetAdminData()
    {
        return Ok(new { message = "Bạn đang truy cập dữ liệu bảo mật dành riêng cho Quản trị viên (Administrator)." });
    }

    /// <summary>
    /// API Bảo mật yêu cầu Quyền Cư dân (Resident).
    /// GET api/sample/resident-only
    /// </summary>
    [HttpGet("resident-only")]
    [Authorize(Policy = ApiPolicies.Resident)]
    public IActionResult GetResidentData()
    {
        return Ok(new { message = "Bạn đang truy cập dữ liệu bảo mật dành riêng cho Cư dân (Resident)." });
    }
}
