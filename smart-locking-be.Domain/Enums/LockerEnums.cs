namespace smart_locking_be.Domain.Enums;

/// <summary>
/// Trạng thái hoạt động hành chính của tòa nhà.
/// </summary>
public enum BuildingStatus
{
    /// <summary>
    /// Tòa nhà đang hoạt động trong hệ thống.
    /// </summary>
    Active,

    /// <summary>
    /// Tòa nhà tạm ngừng hoặc chưa kích hoạt trong hệ thống.
    /// </summary>
    Inactive
}

/// <summary>
/// Trạng thái hoạt động hành chính của cụm locker.
/// </summary>
public enum LockerClusterStatus
{
    /// <summary>
    /// Cụm locker đang hoạt động.
    /// </summary>
    Active,

    /// <summary>
    /// Cụm locker tạm dừng hoạt động.
    /// </summary>
    Inactive
}

/// <summary>
/// Trạng thái vận hành do hệ thống hoặc Nhân viên vận hành quản lý cho locker.
/// </summary>
public enum LockerOperationalStatus
{
    /// <summary>
    /// Locker sẵn sàng vận hành phục vụ giao nhận hàng.
    /// </summary>
    Operational,

    /// <summary>
    /// Locker đang tạm ngưng phục vụ do bảo trì hoặc sự cố.
    /// </summary>
    OutOfService,

    /// <summary>
    /// Locker bị vô hiệu hóa hành chính.
    /// </summary>
    Inactive
}

/// <summary>
/// Trạng thái kết nối mạng/IoT của thiết bị locker với hệ thống backend.
/// </summary>
public enum LockerConnectionStatus
{
    /// <summary>
    /// Thiết bị locker đang duy trì kết nối mạng ổn định với backend.
    /// </summary>
    Online,

    /// <summary>
    /// Thiết bị locker đã mất kết nối mạng với backend.
    /// </summary>
    Offline,

    /// <summary>
    /// Chưa xác định được trạng thái kết nối mạng của thiết bị locker.
    /// </summary>
    Unknown
}

/// <summary>
/// Trạng thái vận hành của từng ngăn locker vật lý.
/// </summary>
public enum LockerCompartmentOperationalStatus
{
    /// <summary>
    /// Ngăn locker hoạt động bình thường, sẵn sàng phân bổ.
    /// </summary>
    Operational,

    /// <summary>
    /// Ngăn locker tạm ngừng phục vụ do hỏng hóc hoặc đang bảo trì.
    /// </summary>
    OutOfService,

    /// <summary>
    /// Ngăn locker bị vô hiệu hóa.
    /// </summary>
    Inactive
}

/// <summary>
/// Trạng thái cảm biến cửa ngăn locker.
/// </summary>
public enum DoorStatus
{
    /// <summary>
    /// Cửa ngăn locker đang mở.
    /// </summary>
    Open,

    /// <summary>
    /// Cửa ngăn locker đang đóng kín.
    /// </summary>
    Closed,

    /// <summary>
    /// Cảm biến cửa không phản hồi hoặc trạng thái cửa chưa xác định.
    /// </summary>
    Unknown
}

/// <summary>
/// Mục đích/ngữ cảnh của thao tác mở tủ locker.
/// </summary>
public enum LockerAccessType
{
    /// <summary>
    /// Shipper mở ngăn locker để gửi hàng cho Cư dân.
    /// </summary>
    ShipperDropOff,

    /// <summary>
    /// Cư dân mở ngăn locker để lấy hàng nhập.
    /// </summary>
    ResidentPickup,

    /// <summary>
    /// Cư dân mở ngăn locker để bỏ hàng gửi trả vào locker.
    /// </summary>
    ResidentReturnDropOff,

    /// <summary>
    /// Shipper mở ngăn locker để lấy hàng gửi trả của Cư dân.
    /// </summary>
    ShipperReturnPickup,

    /// <summary>
    /// Nhân viên vận hành mở khóa khẩn cấp kiểm tra/giải quyết sự cố.
    /// </summary>
    OperatorEmergency,

    /// <summary>
    /// Nhân viên mở tủ thực hiện thao tác bảo trì kỹ thuật.
    /// </summary>
    Maintenance
}

/// <summary>
/// Phương thức xác thực / quyền sử dụng để mở tủ locker.
/// </summary>
public enum LockerAccessMethod
{
    /// <summary>
    /// Thao tác theo phiên làm việc khách của Shipper (Guest Session Token).
    /// </summary>
    GuestSession,

    /// <summary>
    /// Nhập mã OTP xác thực.
    /// </summary>
    Otp,

    /// <summary>
    /// Quét mã QR cá nhân của Cư dân.
    /// </summary>
    PersonalQr,

    /// <summary>
    /// Mở ngăn từ xa qua ứng dụng di động của Cư dân.
    /// </summary>
    RemoteApp,

    /// <summary>
    /// Xác thực sinh trắc học bằng nhận diện khuôn mặt tại kiosk.
    /// </summary>
    FaceRecognition,

    /// <summary>
    /// Quyền xác thực và ủy quyền trực tiếp của Nhân viên vận hành.
    /// </summary>
    OperatorAuthorization,

    /// <summary>
    /// Quyền tự động ủy quyền trực tiếp từ hệ thống.
    /// </summary>
    SystemAuthorization
}

/// <summary>
/// Kết quả của thao tác truy cập/mở tủ locker.
/// </summary>
public enum LockerAccessResult
{
    /// <summary>
    /// Thao tác truy cập và mở cửa ngăn thành công.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Thao tác thất bại do thông tin xác thực không hợp lệ.
    /// </summary>
    Failed,

    /// <summary>
    /// Thao tác bị hệ thống chặn do vi phạm quy tắc bảo mật hoặc nợ phí quá hạn.
    /// </summary>
    Blocked
}
