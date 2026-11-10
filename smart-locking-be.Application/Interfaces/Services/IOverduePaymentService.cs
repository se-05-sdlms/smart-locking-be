using smart_locking_be.Application.DTOs.Payments;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOverduePaymentService
{
    Task<OverduePaymentResponse> CreateCheckoutAsync(Guid residentUserId, Guid chargeId, CancellationToken cancellationToken = default);
    Task<OverduePaymentResponse> GetAsync(Guid residentUserId, Guid transactionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OverduePaymentResponse>> GetHistoryAsync(Guid residentUserId, CancellationToken cancellationToken = default);
    Task<bool> TryCompleteAsync(PayOSWebhookData payment, CancellationToken cancellationToken = default);
}
