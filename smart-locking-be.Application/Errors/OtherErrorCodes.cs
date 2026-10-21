namespace smart_locking_be.Application.Errors;

public static class IncidentErrorCodes
{
    public const string NotFound = "incident/not_found";
    public const string InvalidLink = "incident/invalid_link";
    public const string InvalidState = "incident/invalid_state";
    public const string AccessDenied = "incident/access_denied";
}

public static class ParcelErrorCodes
{
    public const string NotFound = "parcel/not_found";
    public const string InvalidState = "parcel/invalid_state";
    public const string AccessDenied = "parcel/access_denied";
    public const string PaymentRequired = "parcel/payment_required";
    public const string StorageExpired = "parcel/storage_expired";
    public const string RemoteUnlockDisabled = "parcel/remote_unlock_disabled";
    public const string InvalidAccessEvent = "parcel/invalid_access_event";
}

public static class UserErrorCodes
{
    public const string NotFound = "user/not_found";
    public const string AlreadyExists = "user/already_exists";
    public const string Inactive = "user/inactive";
    public const string AssignmentConflict = "user/assignment_conflict";
    public const string ResidentProfileReadOnly = "user/resident_profile_read_only";
    public const string EmailUnavailable = "user/email_unavailable";
}

public static class SubscriptionErrorCodes
{
    public const string NotFound = "subscription/not_found";
    public const string AlreadyExists = "subscription/already_exists";
    public const string InvalidPayment = "subscription/invalid_payment";
    public const string InvalidState = "subscription/invalid_state";
}

public static class PolicyErrorCodes
{
    public const string NotFound = "policy/not_found";
    public const string Immutable = "policy/immutable";
}

public static class NotificationErrorCodes
{
    public const string NotFound = "notification/not_found";
    public const string RuleAlreadyExists = "notification/rule_already_exists";
}

public static class PaymentErrorCodes
{
    public const string InvalidRequest = "payment/invalid_request";
    public const string InvalidWebhook = "payment/invalid_webhook";
    public const string Unavailable = "payment/unavailable";
    public const string NotFound = "payment/not_found";
}

public static class MaintenanceErrorCodes
{
    public const string NotFound = "maintenance/not_found";
    public const string InvalidRequest = "maintenance/invalid_request";
}

public static class DeviceErrorCodes
{
    public const string NotFound = "device/not_found";
    public const string TokenAlreadyRegistered = "device/token_already_registered";
    public const string InvalidToken = "device/invalid_token";
}

public static class ImageErrorCodes
{
    public const string InvalidImage = "image/invalid_image";
    public const string InvalidFormat = "image/invalid_format";
    public const string TooLarge = "image/too_large";
    public const string StorageUnavailable = "image/storage_unavailable";
}

public static class OperationalRecordErrorCodes
{
    public const string NotFound = "operational_record/not_found";
}

public static class AuditErrorCodes
{
    public const string NotFound = "audit/not_found";
}
