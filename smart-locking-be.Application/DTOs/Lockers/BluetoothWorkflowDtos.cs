namespace smart_locking_be.Application.DTOs.Lockers;

// D: shipper gửi, P: resident nhận, R: resident gửi, G: guest nhận; N: tạo nháp resident gửi rồi chạy R.
// DeviceIdentifier là thiết bị ESP đang kết nối, khác mã tủ dùng trong QR/tra cứu nghiệp vụ.
public sealed record BluetoothPrepareRequest(string Kind, Guid SubjectId, string? GuestSessionToken = null, string? DeviceIdentifier = null);
// Có Command chỉ nghĩa là đã chuẩn bị lệnh; cần nhật ký mở/đóng cửa để xác nhận gửi/nhận xong.
public sealed record BluetoothCommandResponse(string DeviceIdentifier, int Channel, string Command, DateTimeOffset ExpiresAt);
// Permit lưu danh tính cư dân để thử offline; DeviceIdentifier="*" cho phép chọn tủ khi kết nối.
public sealed record ResidentBluetoothPermitResponse(string DeviceIdentifier, string Permit, DateTimeOffset ExpiresAt);
// URL ảnh đã upload; không gửi nội dung ảnh hoặc trạng thái cửa qua DTO này.
public sealed record OfflineReturnImageRequest(string ImageUrl);
