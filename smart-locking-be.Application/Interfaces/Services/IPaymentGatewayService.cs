using smart_locking_be.Application.DTOs.Payments;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IPaymentGatewayService
{
    Task<CreatePaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken = default);
    Task<string> GetPaymentLinkStatusAsync(long orderCode, CancellationToken cancellationToken = default);
    Task<string> CancelPaymentLinkAsync(long orderCode, CancellationToken cancellationToken = default);
    PayOSWebhookData VerifyWebhook(PayOSWebhookPayload payload);
}
