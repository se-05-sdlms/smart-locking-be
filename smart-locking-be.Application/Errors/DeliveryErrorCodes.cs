namespace smart_locking_be.Application.Errors;

public static class DeliveryErrorCodes
{
    public const string NotFound = "delivery/not_found";
    public const string InvalidState = "delivery/invalid_state";
    public const string ApprovalExpired = "delivery/approval_expired";
    public const string SessionExpired = "delivery/session_expired";
    public const string AccessDenied = "delivery/access_denied";
    public const string OcrLimitReached = "delivery/ocr_limit_reached";
    public const string LookupLimitReached = "delivery/lookup_limit_reached";
    public const string ParcelImageRequired = "delivery/parcel_image_required";
    public const string RecipientNotFound = "delivery/recipient_not_found";
    public const string RecipientCannotReceive = "delivery/recipient_cannot_receive";
}
