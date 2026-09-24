using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Tài khoản đăng nhập dùng chung cho Cư dân, Nhân viên vận hành và Quản trị viên.
/// </summary>
public sealed class User
{
    /// <summary>
    /// Mã định danh duy nhất của tài khoản người dùng.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Số điện thoại đã được chuẩn hóa dùng cho đăng nhập, xác minh và liên hệ; có thể null với tài khoản nội bộ chỉ dùng email.
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Địa chỉ email đã được chuẩn hóa dùng cho đăng nhập hoặc nhận thông báo; có thể null khi tài khoản chỉ dùng số điện thoại.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Giá trị băm mật khẩu; tuyệt đối không lưu mật khẩu dạng rõ.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái tài khoản quyết định người dùng có được phép đăng nhập và sử dụng chức năng được bảo vệ hay không.
    /// </summary>
    public UserStatus Status { get; set; }

    /// <summary>
    /// Vai trò cố định của người dùng trong hệ thống (Administrator, Resident, LockerOperator).
    /// </summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// Thời điểm xác minh quyền sở hữu số điện thoại thành công; null nếu chưa xác minh hoặc tài khoản không dùng phone verification.
    /// </summary>
    public DateTimeOffset? PhoneVerifiedAt { get; set; }

    /// <summary>
    /// Cho biết người dùng bắt buộc đổi mật khẩu ở lần đăng nhập phù hợp, đặc biệt với tài khoản do Administrator tạo.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Thời điểm đăng nhập thành công gần nhất.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Thời điểm tài khoản được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm thông tin tài khoản được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Hồ sơ Resident gắn 1-1 với User khi tài khoản mang nghiệp vụ Resident.
    /// </summary>
    public ResidentProfile? ResidentProfile { get; set; }

    /// <summary>
    /// Các phạm vi vận hành locker được giao cho User khi User là Locker Operator.
    /// </summary>
    public ICollection<OperatorAssignment> OperatorAssignments { get; set; } = new List<OperatorAssignment>();

    /// <summary>
    /// Các phân công Locker Operator do User này tạo.
    /// </summary>
    public ICollection<OperatorAssignment> CreatedOperatorAssignments { get; set; } = new List<OperatorAssignment>();

    /// <summary>
    /// Các phiên bản policy do User này tạo.
    /// </summary>
    public ICollection<SystemPolicy> CreatedSystemPolicies { get; set; } = new List<SystemPolicy>();

    /// <summary>
    /// Các OTP challenge liên quan đến tài khoản.
    /// </summary>
    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();

    /// <summary>
    /// Các Refresh Token đã phát hành cho tài khoản.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    /// <summary>
    /// Các parcel đã được User này xử lý remove/clear với quyền vận hành.
    /// </summary>
    public ICollection<Parcel> RemovedParcels { get; set; } = new List<Parcel>();

    /// <summary>
    /// Các lịch sử thay đổi trạng thái parcel do User này thực hiện.
    /// </summary>
    public ICollection<ParcelStatusHistory> ParcelStatusChanges { get; set; } = new List<ParcelStatusHistory>();

    /// <summary>
    /// Lịch sử các lần User này thử hoặc thực hiện truy cập / mở tủ locker.
    /// </summary>
    public ICollection<LockerAccessEvent> LockerAccessEvents { get; set; } = new List<LockerAccessEvent>();

    /// <summary>
    /// Các thông báo gửi cho User.
    /// </summary>
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    /// <summary>
    /// Các thiết bị đã đăng ký nhận push notification của User.
    /// </summary>
    public ICollection<DeviceInstallation> DeviceInstallations { get; set; } = new List<DeviceInstallation>();

    /// <summary>
    /// Các incident do User đã đăng nhập báo cáo.
    /// </summary>
    public ICollection<Incident> ReportedIncidents { get; set; } = new List<Incident>();

    /// <summary>
    /// Các incident được giao cho User xử lý.
    /// </summary>
    public ICollection<Incident> AssignedIncidents { get; set; } = new List<Incident>();

    /// <summary>
    /// Các action trên incident do User thực hiện.
    /// </summary>
    public ICollection<IncidentAction> IncidentActions { get; set; } = new List<IncidentAction>();

    /// <summary>
    /// Các locker event có actor là User.
    /// </summary>
    public ICollection<LockerEvent> LockerEvents { get; set; } = new List<LockerEvent>();

    /// <summary>
    /// Các thao tác emergency unlock do User thực hiện.
    /// </summary>
    public ICollection<EmergencyUnlock> EmergencyUnlocks { get; set; } = new List<EmergencyUnlock>();

    /// <summary>
    /// Các maintenance request do User tạo.
    /// </summary>
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();

    /// <summary>
    /// Các activity trong maintenance do User thực hiện.
    /// </summary>
    public ICollection<MaintenanceActivity> MaintenanceActivities { get; set; } = new List<MaintenanceActivity>();

    /// <summary>
    /// Các audit record có actor là User.
    /// </summary>
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
