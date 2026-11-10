using Microsoft.Extensions.Options;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Infrastructure.Options;
using System.Net;
using System.Net.Http.Headers;

namespace smart_locking_be.Infrastructure.Services;

public sealed class SupabaseImageStorageService(
    HttpClient httpClient,
    IOptions<SupabaseStorageOptions> options) : IImageStorageService
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
        if (!AllowedExtensions.Contains(extension) || !IsAllowedContentType(contentType, extension))
        {
            throw new BadRequestException(ImageErrorCodes.InvalidFormat, "Image must be JPG, PNG, or WEBP.");
        }

        byte[] header = new byte[12];
        int read = await image.ReadAtLeastAsync(header, 12, false, cancellationToken);
        if (!HasValidSignature(header.AsSpan(0, read), extension))
        {
            throw new BadRequestException(ImageErrorCodes.InvalidFormat, "File content is not a supported image format.");
        }

        // The upload endpoint limits images to 8 MB. Replay the header without requiring a seekable input.
        using var payload = new MemoryStream();
        await payload.WriteAsync(header.AsMemory(0, read), cancellationToken);
        await image.CopyToAsync(payload, cancellationToken);
        payload.Position = 0;

        SupabaseStorageOptions settings = options.Value;
        string storedName = $"{Guid.NewGuid():N}{extension}";
        string folder = string.IsNullOrWhiteSpace(settings.Folder) ? string.Empty : settings.Folder;
        string objectPath = string.Join('/', folder.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Append(storedName).Select(Uri.EscapeDataString));
        string storageUrl = $"{settings.Url.TrimEnd('/')}/storage/v1/object";
        string encodedPath = $"{Uri.EscapeDataString(settings.BucketName)}/{objectPath}";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{storageUrl}/{encodedPath}");
        request.Headers.Add("apikey", settings.ApiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new StreamContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                throw new BadRequestException(ImageErrorCodes.TooLarge, "Image exceeds the storage size limit.");
            }

            if (!response.IsSuccessStatusCode)
            {
                // Never expose provider response bodies, keys, or request URLs to clients.
                throw new ServiceUnavailableException(
                    ImageErrorCodes.StorageUnavailable, "Image storage is currently unavailable.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceUnavailableException(
                ImageErrorCodes.StorageUnavailable, "Image storage is currently unavailable.");
        }
        catch (HttpRequestException exception)
        {
            throw new ServiceUnavailableException(
                ImageErrorCodes.StorageUnavailable, "Image storage is currently unavailable.",
                exception);
        }

        return $"{storageUrl}/public/{encodedPath}";
    }

    private static bool IsAllowedContentType(string contentType, string extension) =>
        string.Equals(contentType, extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        }, StringComparison.OrdinalIgnoreCase);

    private static bool HasValidSignature(ReadOnlySpan<byte> header, string extension) => extension switch
    {
        ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
