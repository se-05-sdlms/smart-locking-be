using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/images"), AllowAnonymous, EnableRateLimiting(RateLimitPolicyNames.Upload)]
public sealed class ImagesController(IImageStorageService imageStorageService) : ControllerBase
{
    [HttpPost, HttpPost("/api/uploads/images"), RequestSizeLimit(8 * 1024 * 1024)]
    [RequestTimeout(RequestTimeoutPolicyNames.Upload)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > 8 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh phải là JPG, PNG hoặc WEBP và không vượt quá 8 MB." });
        try
        {
            await using Stream stream = file.OpenReadStream();
            string path = await imageStorageService.UploadAsync(stream, file.FileName, file.ContentType, cancellationToken);
            string url = Uri.IsWellFormedUriString(path, UriKind.Absolute)
                ? path
                : $"{Request.Scheme}://{Request.Host}{path}";
            return StatusCode(StatusCodes.Status201Created, new { url });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Dịch vụ lưu trữ ảnh hiện không khả dụng. Vui lòng thử lại sau." });
        }
    }
}
