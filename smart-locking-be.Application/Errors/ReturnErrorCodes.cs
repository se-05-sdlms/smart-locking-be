namespace smart_locking_be.Application.Errors;

public static class ReturnErrorCodes
{
    public const string NotFound = "return/not_found";
    public const string InvalidState = "return/invalid_state";
    public const string InvalidPickupCode = "return/invalid_pickup_code";
    public const string SessionExpired = "return/session_expired";
}
