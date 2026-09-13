using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho quy trình gửi hàng khách tại locker (guest drop-off) từ khi bắt đầu phiên đến khi gửi kiện thành công hoặc kết thúc.
/// </summary>
public sealed class DeliveryRequest
{
    /// <summary>
    /// Mã định danh duy nhất của yêu cầu giao hàng / phiên gửi hàng.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh Cư dân nhận hàng được xác định từ số điện thoại; null khi phiên mới khởi tạo chưa nhập/resolve được Cư dân.
    /// </summary>
    public Guid? ResidentProfileId { get; set; }

    /// <summary>
    /// Mã định danh cụm locker nơi Shipper đang thực hiện gửi hàng.
    /// </summary>
    public Guid LockerClusterId { get; set; }

    /// <summary>
    /// Mã định danh phiên bản chính sách được snapshot cho yêu cầu giao hàng.
    /// </summary>
    public Guid SystemPolicyId { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker được phân bổ cho yêu cầu; null trước khi phân bổ thành công.
    /// </summary>
    public Guid? AllocatedCompartmentId { get; set; }

    /// <summary>
    /// Giá trị băm của token phiên gửi hàng khách; không lưu raw token.
    /// </summary>
    public string GuestSessionTokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Họ tên của Shipper nếu quy trình thu thập.
    /// </summary>
    public string? ShipperName { get; set; }

    /// <summary>
    /// Số điện thoại của Shipper nếu quy trình thu thập.
    /// </summary>
    public string? ShipperPhone { get; set; }

    /// <summary>
    /// Số điện thoại Cư dân nhận hàng do Shipper xác nhận; null ở bước đầu phiên.
    /// </summary>
    public string? RecipientPhoneSnapshot { get; set; }

    /// <summary>
    /// Đường dẫn/URL của ảnh chụp đơn hàng/kiện hàng do Shipper chụp làm bằng chứng gửi hàng; Cư dân có thể xem khi duyệt yêu cầu.
    /// </summary>
    public string? ParcelImageUrl { get; set; }

    /// <summary>
    /// Số điện thoại bóc tách từ vận đơn bằng OCR trước khi Shipper xác nhận/chỉnh sửa.
    /// </summary>
    public string? OcrExtractedPhone { get; set; }

    /// <summary>
    /// Trạng thái kết quả nhận dạng OCR.
    /// </summary>
    public OcrStatus? OcrStatus { get; set; }

    /// <summary>
    /// Chế độ phê duyệt của Cư dân tại thời điểm resolve Cư dân; null trước khi xác định Cư dân.
    /// </summary>
    public DeliveryApprovalMode? ApprovalModeSnapshot { get; set; }

    /// <summary>
    /// Trạng thái hiện tại của quy trình gửi hàng.
    /// </summary>
    public DeliveryRequestStatus Status { get; set; }

    /// <summary>
    /// Thời điểm diễn ra thao tác gần nhất của Shipper dùng để tính timeout không hoạt động.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>
    /// Thời điểm hết hạn phiên làm việc của Shipper nếu không có tương tác mới.
    /// </summary>
    public DateTimeOffset SessionExpiresAt { get; set; }

    /// <summary>
    /// Thời hạn Cư dân phải phản hồi phê duyệt thủ công; null nếu không thuộc luồng phê duyệt thủ công.
    /// </summary>
    public DateTimeOffset? ApprovalExpiresAt { get; set; }

    /// <summary>
    /// Thời hạn giữ đặt trước ngăn locker sau khi phân bổ; null trước khi phân bổ ngăn.
    /// </summary>
    public DateTimeOffset? ReservationExpiresAt { get; set; }

    /// <summary>
    /// Thời điểm Cư dân hoặc hệ thống đưa ra quyết định phê duyệt hoặc từ chối.
    /// </summary>
    public DateTimeOffset? DecisionAt { get; set; }

    /// <summary>
    /// Thời điểm ngăn locker được phân bổ thành công cho yêu cầu.
    /// </summary>
    public DateTimeOffset? AllocatedAt { get; set; }

    /// <summary>
    /// Thời điểm xác nhận kiện hàng đã nằm trong ngăn locker và cửa ngăn đã đóng.
    /// </summary>
    public DateTimeOffset? DepositedAt { get; set; }

    /// <summary>
    /// Thời điểm ngăn locker được giải phóng để tái sử dụng; null khi yêu cầu vẫn giữ ngăn.
    /// </summary>
    public DateTimeOffset? CompartmentReleasedAt { get; set; }

    /// <summary>
    /// Mã nguyên nhân thất bại khi Status là Failed.
    /// </summary>
    public DeliveryRequestFailureCode? FailureCode { get; set; }

    /// <summary>
    /// Thông tin chi tiết nguyên nhân thất bại phục vụ vận hành và kiểm tra sự cố.
    /// </summary>
    public string? FailureDetail { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu giao hàng / phiên làm việc được khởi tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu giao hàng được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Hồ sơ Cư dân nhận hàng; optional ở giai đoạn đầu phiên chưa resolve người nhận.
    /// </summary>
    public ResidentProfile? ResidentProfile { get; set; }

    /// <summary>
    /// Cụm locker nơi diễn ra quá trình gửi hàng.
    /// </summary>
    public LockerCluster LockerCluster { get; set; } = null!;

    /// <summary>
    /// Phiên bản chính sách áp dụng cho yêu cầu giao hàng.
    /// </summary>
    public SystemPolicy SystemPolicy { get; set; } = null!;

    /// <summary>
    /// Ngăn locker được phân bổ cho yêu cầu giao hàng.
    /// </summary>
    public LockerCompartment? AllocatedCompartment { get; set; }

    /// <summary>
    /// Kiện hàng được tạo khi quá trình gửi hàng hoàn thành thành công.
    /// </summary>
    public Parcel? Parcel { get; set; }

    /// <summary>
    /// Các lượt giữ đặt trước ngăn locker tạo ra cho yêu cầu giao hàng này.
    /// </summary>
    public ICollection<CompartmentReservation> Reservations { get; set; } = new List<CompartmentReservation>();

    /// <summary>
    /// Lịch sử các lần mở cửa/xác thực liên quan đến yêu cầu giao hàng này.
    /// </summary>
    public ICollection<LockerAccessEvent> AccessEvents { get; set; } = new List<LockerAccessEvent>();

    /// <summary>
    /// Các sự cố phát sinh trong quá trình gửi hàng.
    /// </summary>
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
