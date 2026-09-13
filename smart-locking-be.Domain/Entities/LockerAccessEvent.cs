using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Ghi nhận chi tiết mọi thao tác mở hoặc xác thực mở ngăn locker cho tất cả các luồng nghiệp vụ.
/// </summary>
public sealed class LockerAccessEvent
{
    /// <summary>
    /// Mã định danh duy nhất của sự kiện truy cập/mở tủ locker.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh tủ locker chịu tác động mở cửa.
    /// </summary>
    public Guid LockerId { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker cụ thể được mở hoặc xác thực mở.
    /// </summary>
    public Guid LockerCompartmentId { get; set; }

    /// <summary>
    /// Mã định danh người dùng thực hiện thao tác (Resident/Operator) nếu có tài khoản; null đối với Shipper khách.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Mã định danh yêu cầu giao hàng liên quan nếu thao tác thuộc luồng Shipper gửi hàng; null với các luồng khác.
    /// </summary>
    public Guid? DeliveryRequestId { get; set; }

    /// <summary>
    /// Mã định danh kiện hàng liên quan nếu thao tác thuộc luồng Cư dân lấy hàng; null với các luồng khác.
    /// </summary>
    public Guid? ParcelId { get; set; }

    /// <summary>
    /// Mã định danh yêu cầu trả hàng liên quan nếu thao tác thuộc luồng trả hàng; null với các luồng khác.
    /// </summary>
    public Guid? ReturnRequestId { get; set; }

    /// <summary>
    /// Mục đích / ngữ cảnh nghiệp vụ của thao tác mở tủ (Gửi hàng, Lấy hàng, Trả hàng, Khẩn cấp, Bảo trì).
    /// </summary>
    public LockerAccessType AccessType { get; set; }

    /// <summary>
    /// Phương thức xác thực / ủy quyền được sử dụng để mở tủ.
    /// </summary>
    public LockerAccessMethod AccessMethod { get; set; }

    /// <summary>
    /// Kết quả của lượt mở tủ (Succeeded, Failed, Blocked).
    /// </summary>
    public LockerAccessResult Result { get; set; }

    /// <summary>
    /// Ghi chú nguyên nhân thất bại hoặc nguyên nhân bị chặn mở cửa nếu có.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Địa chỉ IP của thiết bị client gửi yêu cầu nếu có.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Thông tin ngữ cảnh thiết bị / kiosk / ứng dụng thực hiện thao tác.
    /// </summary>
    public string? DeviceContext { get; set; }

    /// <summary>
    /// Thời điểm thực tế diễn ra thao tác truy cập / mở cửa locker.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Tủ locker chịu tác động.
    /// </summary>
    public Locker Locker { get; set; } = null!;

    /// <summary>
    /// Ngăn locker chịu tác động.
    /// </summary>
    public LockerCompartment LockerCompartment { get; set; } = null!;

    /// <summary>
    /// Người dùng thực hiện thao tác mở tủ nếu có.
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Yêu cầu giao hàng liên quan nếu có.
    /// </summary>
    public DeliveryRequest? DeliveryRequest { get; set; }

    /// <summary>
    /// Kiện hàng liên quan nếu có.
    /// </summary>
    public Parcel? Parcel { get; set; }

    /// <summary>
    /// Yêu cầu trả hàng liên quan nếu có.
    /// </summary>
    public ReturnRequest? ReturnRequest { get; set; }
}
