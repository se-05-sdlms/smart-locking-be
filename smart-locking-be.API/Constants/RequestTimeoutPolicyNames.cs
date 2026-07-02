namespace smart_locking_be.API.Constants;

public static class RequestTimeoutPolicyNames
{
    public const string Default = "DefaultTimeout";
    public const string Auth = "AuthTimeout";
    public const string Search = "SearchTimeout";
    public const string Upload = "UploadTimeout";
    public const string HeavyAction = "HeavyActionTimeout";
    public const string DeviceCommand = "DeviceCommandTimeout";
}
