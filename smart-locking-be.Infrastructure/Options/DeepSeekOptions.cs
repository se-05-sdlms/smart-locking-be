namespace smart_locking_be.Infrastructure.Options;

public sealed class DeepSeekOptions
{
    public const string SectionName = "DeepSeek";

    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    public int TimeoutSeconds { get; set; } = 8;
}
