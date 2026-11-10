using smart_locking_be.Domain.Enums;

namespace smart_locking_be.API.Authorization;

/// <summary>
/// Khai báo các hằng số Policy phân quyền cho API dựa trên vai trò (UserRole).
/// Sử dụng tại Controller qua attribute [Authorize(Policy = ApiPolicies.Administrator)]
/// để tránh gõ sai chuỗi cứng (magic string).
/// </summary>
public static class ApiPolicies
{
    /// <summary>
    /// Quyền Quản trị viên hệ thống (Administrator).
    /// Được phép truy cap các API quản trị hệ thống, cấu hình locker, người dùng.
    /// </summary>
    public const string Administrator = nameof(UserRole.Administrator);

    /// <summary>
    /// Quyền Cư dân (Resident).
    /// Được phép truy cập các API dành cho cư dân gửi/nhận hàng tại tủ locker.
    /// </summary>
    public const string Resident = nameof(UserRole.Resident);

    /// <summary>
    /// Quyền Nhân viên vận hành (LockerOperator).
    /// Được phép truy cập các API vận hành, xử lý sự cố tủ locker.
    /// </summary>
    public const string LockerOperator = nameof(UserRole.LockerOperator);
}
