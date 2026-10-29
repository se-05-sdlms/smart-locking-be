namespace smart_locking_be.Infrastructure.Options;

public sealed class LockerCommissioningOptions
{
    public const string SectionName = "LockerCommissioning";
    public string FirmwareVersion { get; set; } = string.Empty;
    public string HardwareModel { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string InstallationGuideUrl { get; set; } = string.Empty;
    public int ObservationFreshnessSeconds { get; set; }
}
