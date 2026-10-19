namespace smart_locking_be.Application.Constants;

/// <summary>Tên sự kiện audit dùng chung giữa use case và adapter lưu trữ; giữ ổn định dữ liệu đã ghi.</summary>
public static class CompartmentResetActions
{
    public const string Requested = "LockerCompartment.ResetRequested";
    public const string Rejected = "LockerCompartment.ResetRejected";
}
