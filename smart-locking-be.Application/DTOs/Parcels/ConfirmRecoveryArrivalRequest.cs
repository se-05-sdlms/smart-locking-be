namespace smart_locking_be.Application.DTOs.Parcels;

/// <summary>Xác nhận hàng đã tới điểm tập kết, tách biệt với yêu cầu reset/PIN.</summary>
public sealed record ConfirmRecoveryArrivalRequest(Guid ResetCommandId, bool ConfirmedArrived);
