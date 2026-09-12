namespace smart_locking_be.Domain.Enums;

public enum OcrStatus
{
    Succeeded,
    Failed,
    Skipped
}

public enum DeliveryRequestStatus
{
    Started,
    PendingApproval,
    Approved,
    Rejected,
    Expired,
    Allocated,
    Deposited,
    Cancelled,
    Failed
}

public enum DeliveryRequestFailureCode
{
    NoCompartment,
    DoorNotClosed,
    ResidentUnavailable,
    DeviceUnavailable
}

public enum ParcelStatus
{
    Stored,
    Overdue,
    Retrieved,
    Removed
}

public enum ParcelAccessMethod
{
    PersonalQr,
    Otp,
    RemoteUnlock,
    FaceRecognition
}

public enum ParcelAccessResult
{
    Succeeded,
    Failed,
    Blocked
}

public enum OverdueChargeStatus
{
    Outstanding,
    Paid
}

public enum PaymentTransactionStatus
{
    Pending,
    Success,
    Failed,
    Cancelled
}

public enum NotificationDeliveryStatus
{
    Pending,
    Sent,
    Failed
}

public enum NotificationChannel
{
    InApp,
    Push,
    Sms
}
