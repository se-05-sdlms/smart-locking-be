namespace smart_locking_be.Application.Errors;

public static class LockerErrorCodes
{
    public const string NotFound = "locker/not_found";
    public const string NotOperational = "locker/not_operational";
    public const string NoAvailableCompartment = "locker/no_available_compartment";
    public const string AccessDenied = "locker/access_denied";
    public const string InvalidCompartment = "locker/invalid_compartment";
    public const string OpenFailed = "locker/open_failed";
    public const string Offline = "locker/offline";
    public const string CodeAlreadyExists = "locker/code_already_exists";
    public const string DeviceAlreadyExists = "locker/device_already_exists";
    public const string CompartmentAlreadyExists = "locker/compartment_already_exists";
}
