namespace smart_locking_be.Domain.Enums;

/// <summary>
/// Vai trò cố định của tài khoản người dùng trong hệ thống SDLMS.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Quản trị viên hệ thống có toàn quyền quản lý cấu hình, người dùng và giám sát.
    /// </summary>
    Administrator,

    /// <summary>
    /// Cư dân sử dụng dịch vụ tủ locker để nhận và gửi trả hàng.
    /// </summary>
    Resident,

    /// <summary>
    /// Nhân viên vận hành tủ locker trong phạm vi được phân công.
    /// </summary>
    LockerOperator
}

/// <summary>
/// Trạng thái hoạt động của tài khoản người dùng.
/// </summary>
public enum UserStatus
{
    /// <summary>
    /// Tài khoản đang hoạt động bình thường.
    /// </summary>
    Active,

    /// <summary>
    /// Tài khoản bị vô hiệu hóa bởi Quản trị viên.
    /// </summary>
    Disabled,

    /// <summary>
    /// Tài khoản tạm thời bị khóa do vi phạm chính sách bảo mật hoặc nhập sai credentials quá số lần.
    /// </summary>
    Locked
}

/// <summary>
/// Chế độ phê duyệt giao hàng do Cư dân cấu hình.
/// </summary>
public enum DeliveryApprovalMode
{
    /// <summary>
    /// Tự động phê duyệt các yêu cầu giao hàng gửi tới Cư dân.
    /// </summary>
    Auto,

    /// <summary>
    /// Yêu cầu Cư dân xác nhận thủ công cho từng yêu cầu giao hàng trước khi cấp ngăn.
    /// </summary>
    Manual
}

/// <summary>
/// Mục đích sử dụng của mã OTP xác thực.
/// </summary>
public enum OtpPurpose
{
    /// <summary>
    /// Xác minh số điện thoại khi đăng ký tài khoản.
    /// </summary>
    Registration,

    /// <summary>
    /// Đặt lại mật khẩu tài khoản.
    /// </summary>
    PasswordReset,

    /// <summary>
    /// Xác thực quyền lấy kiện hàng tại locker.
    /// </summary>
    ParcelRetrieval
}

/// <summary>
/// Kết quả của thao tác được ghi nhận trong nhật ký kiểm toán (Audit Log).
/// </summary>
public enum AuditLogResult
{
    /// <summary>
    /// Thao tác thực hiện thành công.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Thao tác thất bại.
    /// </summary>
    Failed
}
