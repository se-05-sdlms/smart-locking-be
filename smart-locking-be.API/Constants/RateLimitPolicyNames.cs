namespace smart_locking_be.API.Constants;

public static class RateLimitPolicyNames
{
    public const string PublicApi = "PublicApiPolicy";
    public const string Auth = "AuthPolicy";
    public const string Search = "SearchPolicy";
    public const string Upload = "UploadPolicy";
    public const string HeavyAction = "HeavyActionPolicy";
    public const string DeviceCommand = "DeviceCommandPolicy";
}
