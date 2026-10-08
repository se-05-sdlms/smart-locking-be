using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Services;

public sealed class LocalImageStorageService(IWebHostEnvironment environment) : IImageStorageService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public async Task<string> UploadAsync(
        Stream image,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) || !IsAllowedContentType(contentType))
        {
            throw new ArgumentException("Ảnh phải là JPG, PNG hoặc WEBP.");
        }

        byte[] header = new byte[12];
        int read = await image.ReadAtLeastAsync(header, 12, false, cancellationToken);
        if (!HasValidSignature(header.AsSpan(0, read), extension))
        {
            throw new ArgumentException("Nội dung tệp không đúng định dạng ảnh.");
        }

        string folder = Path.Combine(
            environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"),
            "uploads",
            "images");
        Directory.CreateDirectory(folder);
        string storedName = $"{Guid.NewGuid():N}{extension}";
        await using FileStream destination = File.Create(Path.Combine(folder, storedName));
        await destination.WriteAsync(header.AsMemory(0, read), cancellationToken);
        await image.CopyToAsync(destination, cancellationToken);
        return $"/uploads/images/{storedName}";
    }

    private static bool IsAllowedContentType(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

    private static bool HasValidSignature(ReadOnlySpan<byte> header, string extension) => extension switch
    {
        ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
