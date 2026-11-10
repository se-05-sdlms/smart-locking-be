using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Payments;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IPaymentReconciliationService
{
    Task<PagedResult<PaymentLookupResponse>> GetPaymentsAsync(Guid actorId, PaymentLookupQuery query, CancellationToken cancellationToken = default);
    Task<PaymentLookupResponse> GetPaymentAsync(Guid actorId, string kind, Guid paymentId, CancellationToken cancellationToken = default);
    Task<PagedResult<PaymentReconciliationResponse>> GetCasesAsync(Guid actorId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<PaymentReconciliationResponse> GetCaseAsync(Guid actorId, Guid id, CancellationToken cancellationToken = default);
    Task<PaymentReconciliationResponse> CreateAsync(Guid actorId, CreatePaymentReconciliationRequest request, CancellationToken cancellationToken = default);
    Task<PaymentReconciliationResponse> AssignAsync(Guid actorId, Guid id, CancellationToken cancellationToken = default);
    Task<PaymentReconciliationResponse> RespondAsync(Guid actorId, Guid id, RespondPaymentReconciliationRequest request, CancellationToken cancellationToken = default);
    Task<PaymentReconciliationResponse> SynchronizeAsync(Guid actorId, Guid id, CancellationToken cancellationToken = default);
}
