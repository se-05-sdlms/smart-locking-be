namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>Lệnh có thể đã đến thiết bị dù backend không nhận được phản hồi từ broker.</summary>
public sealed class LockerCommandDeliveryUnknownException(Exception innerException)
    : Exception("Chưa xác nhận được kết quả gửi lệnh; tiếp tục chờ cảm biến cửa.", innerException);
