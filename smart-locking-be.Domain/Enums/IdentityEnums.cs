namespace smart_locking_be.Domain.Enums;

public enum UserStatus
{
    Active,
    Disabled,
    Locked
}

public enum DeliveryApprovalMode
{
    Auto,
    Manual
}

public enum OtpPurpose
{
    Registration,
    PasswordReset,
    ParcelRetrieval
}

public enum AuditLogResult
{
    Succeeded,
    Failed
}
