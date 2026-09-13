namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Nguồn quản lý việc giữ/đặt trước ngăn locker tập trung dùng chung cho cả luồng Giao hàng (Delivery) và Trả hàng (Return).
/// </summary>
public sealed class CompartmentReservation
{
    /// <summary>
    /// Mã định danh duy nhất của lượt giữ đặt trước ngăn locker.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã định danh ngăn locker được giữ đặt trước.
    /// </summary>
    public Guid LockerCompartmentId { get; set; }

    /// <summary>
    /// Mã định danh yêu cầu giao hàng sở hữu lượt giữ ngăn; null nếu lượt giữ thuộc về yêu cầu trả hàng.
    /// </summary>
    public Guid? DeliveryRequestId { get; set; }

    /// <summary>
    /// Mã định danh yêu cầu trả hàng sở hữu lượt giữ ngăn; null nếu lượt giữ thuộc về yêu cầu giao hàng.
    /// </summary>
    public Guid? ReturnRequestId { get; set; }

    /// <summary>
    /// Thời điểm bắt đầu giữ đặt trước ngăn.
    /// </summary>
    public DateTimeOffset ReservedAt { get; set; }

    /// <summary>
    /// Thời điểm hết hạn lượt giữ đặt trước ngăn nếu chưa được giải phóng.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Thời điểm ngăn locker thực tế được giải phóng; null khi lượt giữ ngăn đang còn active.
    /// </summary>
    public DateTimeOffset? ReleasedAt { get; set; }

    /// <summary>
    /// Thời điểm bản ghi giữ đặt trước ngăn được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Ngăn locker được giữ đặt trước.
    /// </summary>
    public LockerCompartment LockerCompartment { get; set; } = null!;

    /// <summary>
    /// Yêu cầu giao hàng sở hữu lượt giữ ngăn nếu có.
    /// </summary>
    public DeliveryRequest? DeliveryRequest { get; set; }

    /// <summary>
    /// Yêu cầu trả hàng sở hữu lượt giữ ngăn nếu có.
    /// </summary>
    public ReturnRequest? ReturnRequest { get; set; }
}
