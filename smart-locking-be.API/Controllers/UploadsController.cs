using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/uploads"), Authorize]
public sealed class UploadsController(IImageStorageService imageStorageService) : ControllerBase
{
    [HttpPost("images"), RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > 8 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh phải là JPG, PNG hoặc WEBP và không vượt quá 8 MB." });
        try
        {
            await using Stream stream = file.OpenReadStream();
            string path = await imageStorageService.UploadAsync(stream, file.FileName, file.ContentType, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new { url = $"{Request.Scheme}://{Request.Host}{path}" });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
