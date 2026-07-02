namespace smart_locking_be.API.Options;

public sealed class RequestTimeoutSettings
{
    public const string SectionName = "RequestTimeouts";

    public int DefaultSeconds { get; init; } = 10;
    public int AuthSeconds { get; init; } = 10;
    public int SearchSeconds { get; init; } = 15;
    public int UploadSeconds { get; init; } = 60;
    public int HeavyActionSeconds { get; init; } = 120;
    public int DeviceCommandSeconds { get; init; } = 15;
}
