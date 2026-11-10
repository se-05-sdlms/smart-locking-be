using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Lockers;

/// <summary>
/// Phản hồi ESP đã được phân tích cú pháp. Service vẫn phải đối chiếu yêu cầu gốc,
/// đúng thiết bị/kênh/giao dịch và kết quả OK trước khi thay đổi nghiệp vụ.
/// </summary>
public sealed record CompartmentResetReceipt(
    Guid CommandId,
    string Device,
    int Channel,
    Guid SubjectId,
    DoorStatus DoorStatus,
    string Result);
