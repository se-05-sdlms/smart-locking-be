namespace smart_locking_be.Infrastructure.Options;

public sealed class PayOSOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string OverdueReturnUrl { get; set; } = string.Empty;
    public string OverdueCancelUrl { get; set; } = string.Empty;
}
