using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/uploads"), Authorize]
public sealed class UploadsController(IWebHostEnvironment environment) : ControllerBase
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    [HttpPost("image"), HttpPost("return-image"), RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> ReturnImage(IFormFile file, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(file.FileName);
        if (file.Length == 0 || file.Length > 8 * 1024 * 1024 || !Allowed.Contains(extension))
            return BadRequest(new { message = "Ảnh phải là JPG, PNG hoặc WEBP và không vượt quá 8 MB." });
        string folder = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "returns");
        Directory.CreateDirectory(folder);
        string name = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using FileStream stream = System.IO.File.Create(Path.Combine(folder, name));
        await file.CopyToAsync(stream, cancellationToken);
        return Ok(new { url = $"{Request.Scheme}://{Request.Host}/uploads/returns/{name}" });
    }
}
