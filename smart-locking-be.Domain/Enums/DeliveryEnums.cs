namespace smart_locking_be.Domain.Enums;

/// <summary>
/// Trạng thái xử lý nhận dạng chữ qua ảnh (OCR) vận đơn.
/// </summary>
public enum OcrStatus
{
    /// <summary>
    /// Nhận dạng chữ thành công.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Nhận dạng chữ thất bại.
    /// </summary>
    Failed,

    /// <summary>
    /// Shipper bỏ qua bước OCR để nhập số điện thoại thủ công.
    /// </summary>
    Skipped
}

/// <summary>
/// Trạng thái vòng đời của yêu cầu giao hàng tại locker (guest drop-off).
/// </summary>
public enum DeliveryRequestStatus
{
    /// <summary>
    /// Shipper đã khởi tạo phiên giao hàng khách (guest drop-off session).
    /// </summary>
    Started,

    /// <summary>
    /// Yêu cầu giao hàng đang chờ Cư dân duyệt thủ công.
    /// </summary>
    PendingApproval,

    /// <summary>
    /// Yêu cầu giao hàng đã được phê duyệt (tự động hoặc thủ công).
    /// </summary>
    Approved,

    /// <summary>
    /// Yêu cầu giao hàng đã được phân bổ ngăn locker phù hợp.
    /// </summary>
    Allocated,

    /// <summary>
    /// Kiện hàng đã được đặt vào ngăn locker và cửa ngăn đã đóng thành công.
    /// </summary>
    Deposited,

    /// <summary>
    /// Cư dân từ chối nhận hàng.
    /// </summary>
    Rejected,

    /// <summary>
    /// Yêu cầu giao hàng hết thời gian chờ (phiên, duyệt thủ công hoặc giữ ngăn).
    /// </summary>
    Expired,

    /// <summary>
    /// Shipper chủ động hủy yêu cầu giao hàng trước khi gửi thành công.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Yêu cầu giao hàng thất bại do lỗi kỹ thuật hoặc thiết bị.
    /// </summary>
    Failed
}

/// <summary>
/// Mã nguyên nhân thất bại kỹ thuật hoặc thiết bị của yêu cầu giao hàng.
/// </summary>
public enum DeliveryRequestFailureCode
{
    /// <summary>
    /// Không tìm thấy ngăn locker khả dụng đủ điều kiện kích thước và vận hành.
    /// </summary>
    NoCompartment,

    /// <summary>
    /// Cửa ngăn locker không đóng lại được sau thời hạn quy định.
    /// </summary>
    DoorNotClosed,

    /// <summary>
    /// Không xác định được Cư dân nhận hàng tương ứng với số điện thoại.
    /// </summary>
    ResidentUnavailable,

    /// <summary>
    /// Thiết bị locker mất kết nối hoặc bị lỗi không thể thực thi lệnh mở cửa.
    /// </summary>
    DeviceUnavailable,

    /// <summary>
    /// Phiên giao hàng của Shipper quá thời gian chờ không có thao tác.
    /// </summary>
    SessionExpired,

    /// <summary>
    /// Quá thời hạn Cư dân phản hồi phê duyệt thủ công.
    /// </summary>
    ApprovalExpired,

    /// <summary>
    /// Quá thời hạn giữ ngăn sau khi đã phân bổ ngăn cho Shipper gửi hàng.
    /// </summary>
    ReservationExpired
}

/// <summary>
/// Trạng thái vòng đời của quy trình gửi trả hàng (Return Flow: Cư dân gửi vào locker -> Shipper đến lấy).
/// </summary>
public enum ReturnRequestStatus
{
    /// <summary>
    /// Cư dân vừa tạo yêu cầu trả hàng, chưa đặt giữ ngăn locker.
    /// </summary>
    Created,

    /// <summary>
    /// Yêu cầu trả hàng đã được phân bổ giữ ngăn locker để Cư dân gửi hàng vào.
    /// </summary>
    Allocated,

    /// <summary>
    /// Cư dân đã mở ngăn và đặt hàng trả vào locker thành công, đang chờ Shipper đến lấy.
    /// </summary>
    Deposited,

    /// <summary>
    /// Shipper đã lấy hàng trả khỏi locker thành công và giải phóng ngăn tủ.
    /// </summary>
    Completed,

    /// <summary>
    /// Yêu cầu trả hàng bị hủy bởi Cư dân hoặc hệ thống.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Yêu cầu trả hàng hết thời hạn giữ ngăn hoặc thời hạn quy định.
    /// </summary>
    Expired,

    /// <summary>
    /// Yêu cầu trả hàng gặp lỗi kỹ thuật hoặc sự cố thiết bị.
    /// </summary>
    Failed
}

/// <summary>
/// Trạng thái vòng đời của kiện hàng nhập (inbound) sau khi đã gửi vào locker thành công.
/// </summary>
public enum ParcelStatus
{
    /// <summary>
    /// Kiện hàng đang lưu trữ trong locker trong thời hạn quy định.
    /// </summary>
    Stored,

    /// <summary>
    /// Kiện hàng đã quá hạn lưu trữ miễn phí/tiêu chuẩn, bắt đầu tính phí quá hạn.
    /// </summary>
    Overdue,

    /// <summary>
    /// Kiện hàng đã được Cư dân lấy thành công.
    /// </summary>
    Retrieved,

    /// <summary>
    /// Kiện hàng đã được Nhân viên vận hành tịch thu hoặc giải phóng khỏi ngăn.
    /// </summary>
    Removed
}

/// <summary>
/// Trạng thái thanh toán của khoản phí quá hạn kiện hàng.
/// </summary>
public enum OverdueChargeStatus
{
    /// <summary>
    /// Khoản phí quá hạn còn nợ chưa thanh toán.
    /// </summary>
    Outstanding,

    /// <summary>
    /// Khoản phí quá hạn đã được thanh toán thành công.
    /// </summary>
    Paid
}

/// <summary>
/// Trạng thái của giao dịch thanh toán phí quá hạn.
/// </summary>
public enum PaymentTransactionStatus
{
    /// <summary>
    /// Giao dịch đang chờ xác nhận từ cổng thanh toán.
    /// </summary>
    Pending,

    /// <summary>
    /// Giao dịch thanh toán thành công.
    /// </summary>
    Success,

    /// <summary>
    /// Giao dịch thanh toán thất bại.
    /// </summary>
    Failed,

    /// <summary>
    /// Giao dịch thanh toán bị hủy bởi người dùng hoặc hệ thống.
    /// </summary>
    Cancelled
}

/// <summary>
/// Trạng thái phát gửi thông báo đến người dùng.
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>
    /// Thông báo đang chờ phát gửi.
    /// </summary>
    Pending,

    /// <summary>
    /// Thông báo đã phát gửi thành công đến kênh tương ứng.
    /// </summary>
    Sent,

    /// <summary>
    /// Phát gửi thông báo thất bại.
    /// </summary>
    Failed
}

/// <summary>
/// Kênh phát gửi thông báo trong hệ thống.
/// </summary>
public enum NotificationChannel
{
    /// <summary>
    /// Thông báo nội bộ trong ứng dụng.
    /// </summary>
    InApp,

    /// <summary>
    /// Thông báo đẩy (Push notification) đến thiết bị di động.
    /// </summary>
    Push,

    /// <summary>
    /// Thông báo qua tin nhắn SMS.
    /// </summary>
    Sms,

    /// <summary>
    /// Thông báo qua thư điện tử Email.
    /// </summary>
    Email
}
