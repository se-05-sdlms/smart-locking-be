namespace smart_locking_be.Application.Errors;

public static class DeliveryErrorCodes
{
    public const string NotFound = "delivery/not_found";
    public const string InvalidState = "delivery/invalid_state";
    public const string ApprovalExpired = "delivery/approval_expired";
    public const string SessionExpired = "delivery/session_expired";
    public const string AccessDenied = "delivery/access_denied";
}
