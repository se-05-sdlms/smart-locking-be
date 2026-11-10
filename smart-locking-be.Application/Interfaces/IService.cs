namespace smart_locking_be.Application.Interfaces;

/// <summary>
/// Interface mẫu hướng dẫn khai báo các phương thức nghiệp vụ cho tầng Application.
/// Mọi Service Interface mới nên được đặt trong thư mục Application/Interfaces.
/// </summary>
public interface IService
{
    /// <summary>
    /// Phương thức mẫu lấy thông điệp chào mừng hoặc kiểm tra dữ liệu từ DB.
    /// </summary>
    Task<string> GetWelcomeMessageAsync(CancellationToken cancellationToken = default);
}
