using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho quy trình Cư dân gửi trả hàng qua tủ locker (Return Flow: Resident deposit -> Shipper pickup).
/// </summary>
public sealed class ReturnRequest
{
    /// <summary>
    /// Mã định danh duy nhất của yêu cầu trả hàng.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh hồ sơ Cư dân khởi tạo yêu cầu trả hàng.
    /// </summary>
    public Guid ResidentProfileId { get; set; }

    /// <summary>
    /// Mã định danh kiện hàng gốc nếu yêu cầu trả hàng phát sinh từ một kiện hàng đã nhận; null nếu trả hàng độc lập.
    /// </summary>
    public Guid? OriginalParcelId { get; set; }

    /// <summary>
    /// Mã định danh tủ locker nơi Cư dân thực hiện trả hàng.
    /// </summary>
    public Guid LockerId { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker được phân bổ giữ đặt trước cho Cư dân gửi hàng trả; null trước khi phân bổ.
    /// </summary>
    public Guid? AllocatedCompartmentId { get; set; }

    /// <summary>
    /// Mã nghiệp vụ duy nhất dùng để tra cứu và theo dõi yêu cầu trả hàng.
    /// </summary>
    public string ReturnCode { get; set; } = string.Empty;

    /// <summary>
    /// Lý do Cư dân thực hiện trả hàng.
    /// </summary>
    public string? ReturnReason { get; set; }

    /// <summary>
    /// Đường dẫn/URL ảnh kiện hàng trả do Cư dân chụp bằng chứng nếu có.
    /// </summary>
    public string? ReturnImageUrl { get; set; }

    /// <summary>
    /// Số điện thoại của Shipper đến nhận hàng trả nếu đã xác định.
    /// </summary>
    public string? ShipperPhone { get; set; }

    /// <summary>
    /// Giá trị băm của token phiên làm việc của Shipper khi thực hiện lấy hàng trả nếu dùng guest session token.
    /// </summary>
    public string? ShipperSessionTokenHash { get; set; }

    /// <summary>
    /// Trạng thái vòng đời của quy trình trả hàng.
    /// </summary>
    public ReturnRequestStatus Status { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu trả hàng được khởi tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu trả hàng được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Thời điểm ngăn locker được phân bổ giữ cho Cư dân gửi hàng.
    /// </summary>
    public DateTimeOffset? AllocatedAt { get; set; }

    /// <summary>
    /// Thời hạn Cư dân phải gửi hàng vào ngăn locker sau khi đã được phân bổ ngăn.
    /// </summary>
    public DateTimeOffset? ReservationExpiresAt { get; set; }

    /// <summary>
    /// Thời điểm Cư dân đặt hàng trả vào ngăn và cửa ngăn đã đóng thành công.
    /// </summary>
    public DateTimeOffset? ResidentDepositedAt { get; set; }

    /// <summary>
    /// Thời điểm Shipper mở ngăn lấy hàng trả và cửa ngăn đã đóng thành công.
    /// </summary>
    public DateTimeOffset? ShipperPickedUpAt { get; set; }

    /// <summary>
    /// Thời điểm ngăn locker được giải phóng để tái sử dụng; null khi yêu cầu vẫn giữ ngăn.
    /// </summary>
    public DateTimeOffset? CompartmentReleasedAt { get; set; }

    /// <summary>
    /// Thông tin chi tiết nguyên nhân khi yêu cầu trả hàng thất bại.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Hồ sơ Cư dân tạo yêu cầu trả hàng.
    /// </summary>
    public ResidentProfile ResidentProfile { get; set; } = null!;

    /// <summary>
    /// Kiện hàng gốc liên quan nếu có.
    /// </summary>
    public Parcel? OriginalParcel { get; set; }

    /// <summary>
    /// Tủ locker diễn ra quá trình trả hàng.
    /// </summary>
    public Locker Locker { get; set; } = null!;

    /// <summary>
    /// Ngăn locker được phân bổ cho yêu cầu trả hàng.
    /// </summary>
    public LockerCompartment? AllocatedCompartment { get; set; }

    /// <summary>
    /// Lịch sử giữ đặt trước ngăn locker cho yêu cầu trả hàng.
    /// </summary>
    public ICollection<CompartmentReservation> Reservations { get; set; } = new List<CompartmentReservation>();

    /// <summary>
    /// Lịch sử các lần mở cửa/xác thực liên quan đến yêu cầu trả hàng.
    /// </summary>
    public ICollection<LockerAccessEvent> AccessEvents { get; set; } = new List<LockerAccessEvent>();
}
