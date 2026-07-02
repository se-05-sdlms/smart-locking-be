namespace smart_locking_be.API.Options;

public sealed class FileUploadSettings
{
    public const string SectionName = "FileUpload";

    public long GlobalMaxRequestBodyBytes { get; init; } = 10 * 1024 * 1024;
    public long MultipartBodyLengthLimitBytes { get; init; } = 10 * 1024 * 1024;
    public long AvatarMaxBytes { get; init; } = 2 * 1024 * 1024;
    public long ImageMaxBytes { get; init; } = 5 * 1024 * 1024;
    public long ImportMaxBytes { get; init; } = 10 * 1024 * 1024;
    public string[] AllowedImageContentTypes { get; init; } = ["image/jpeg", "image/png", "image/webp"];
    public string[] AllowedImageExtensions { get; init; } = [".jpg", ".jpeg", ".png", ".webp"];
}
