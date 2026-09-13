using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Lưu vết thao tác mở khóa khẩn cấp do Nhân viên vận hành thực hiện nhằm kiểm soát quyền và kiểm toán.
/// </summary>
public sealed class EmergencyUnlock
{
    /// <summary>
    /// Mã định danh duy nhất của lượt mở khóa khẩn cấp.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh Nhân viên vận hành thực hiện yêu cầu mở khóa khẩn cấp.
    /// </summary>
    public Guid OperatorUserId { get; set; }

    /// <summary>
    /// Mã định danh tủ locker chịu tác động.
    /// </summary>
    public Guid LockerId { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker cụ thể được mở nếu thao tác ở cấp ngăn.
    /// </summary>
    public Guid? LockerCompartmentId { get; set; }

    /// <summary>
    /// Mã định danh sự cố liên quan tạo ngữ cảnh cho yêu cầu mở khóa khẩn cấp nếu có.
    /// </summary>
    public Guid? IncidentId { get; set; }

    /// <summary>
    /// Lý do bắt buộc do Nhân viên vận hành nhập trước khi gửi lệnh mở khóa.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Kết quả xử lý lệnh mở khóa khẩn cấp: Pending, Succeeded hoặc Failed.
    /// </summary>
    public EmergencyUnlockResult Result { get; set; }

    /// <summary>
    /// Thời điểm Nhân viên gửi yêu cầu mở khóa.
    /// </summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>
    /// Thời điểm lệnh mở khóa hoàn tất (thành công hoặc thất bại).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Nhân viên vận hành thực hiện mở khóa.
    /// </summary>
    public User OperatorUser { get; set; } = null!;

    /// <summary>
    /// Tủ locker chịu tác động.
    /// </summary>
    public Locker Locker { get; set; } = null!;

    /// <summary>
    /// Ngăn locker chịu tác động nếu có.
    /// </summary>
    public LockerCompartment? LockerCompartment { get; set; }

    /// <summary>
    /// Sự cố liên quan nếu có.
    /// </summary>
    public Incident? Incident { get; set; }
}
