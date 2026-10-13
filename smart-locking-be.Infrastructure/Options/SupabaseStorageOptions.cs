namespace smart_locking_be.Infrastructure.Options;

public sealed class SupabaseStorageOptions
{
    public const string SectionName = "SupabaseStorage";

    public string Url { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "smart-locking-images";
    public string Folder { get; set; } = "parcels";
}
