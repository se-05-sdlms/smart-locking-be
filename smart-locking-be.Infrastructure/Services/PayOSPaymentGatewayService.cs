using Microsoft.Extensions.Options;
using Net.payOS;
using Net.payOS.Types;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Infrastructure.Options;
using System.Text.Json;

namespace smart_locking_be.Infrastructure.Services;

public sealed class PayOSPaymentGatewayService(IOptions<PayOSOptions> options) : IPaymentGatewayService
{
    public async Task<CreatePaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = CreateClient();
        ArgumentNullException.ThrowIfNull(request);
        if (request.OrderCode <= 0 || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Description)
            || !IsHttpUrl(request.ReturnUrl) || !IsHttpUrl(request.CancelUrl))
            throw new BadRequestException(PaymentErrorCodes.InvalidRequest, "PayOS payment request is invalid.");

        // SDK 1.x has no cancellation overload. Once dispatched, await its result so a
        // cancelled caller does not hide a payment link that the provider has created.
        try
        {
            var result = await client.createPaymentLink(new PaymentData(request.OrderCode, request.Amount,
                request.Description, [], request.CancelUrl, request.ReturnUrl,
                expiredAt: request.ExpiresAt is null ? null : checked((int)request.ExpiresAt.Value.ToUnixTimeSeconds())));
            return new(result.checkoutUrl, result.paymentLinkId, result.qrCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ServiceUnavailableException(
                PaymentErrorCodes.Unavailable, "Payment service is currently unavailable.",
                exception);
        }
    }

    public async Task<string> GetPaymentLinkStatusAsync(long orderCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return (await CreateClient().getPaymentLinkInformation(orderCode)).status;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ServiceUnavailableException(PaymentErrorCodes.Unavailable,
                "Payment status is currently unavailable.", exception);
        }
    }

    public async Task<string> CancelPaymentLinkAsync(long orderCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return (await CreateClient().cancelPaymentLink(orderCode, "Checkout expired")).status;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ServiceUnavailableException(PaymentErrorCodes.Unavailable,
                "Payment cancellation is currently unavailable.", exception);
        }
    }

    public PayOSWebhookData VerifyWebhook(PayOSWebhookPayload payload)
    {
        var client = CreateClient();
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Data.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(payload.Signature))
            throw new BadRequestException(PaymentErrorCodes.InvalidWebhook, "PayOS webhook is invalid.");

        WebhookData data;
        try
        {
            // Deserialize the complete SDK schema, including currency and optional bank
            // fields, because every one of these fields participates in its checksum.
            data = payload.Data.Deserialize<WebhookData>()
                ?? throw new JsonException("Missing payment data.");
            data = client.verifyPaymentWebhookData(new WebhookType(payload.Code, payload.Desc,
                payload.Success, data, payload.Signature));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new BadRequestException(PaymentErrorCodes.InvalidWebhook, "PayOS webhook signature or data is invalid.", ex);
        }

        if (data.orderCode <= 0 || data.amount <= 0 || string.IsNullOrWhiteSpace(data.reference)
            || string.IsNullOrWhiteSpace(data.code) || string.IsNullOrWhiteSpace(data.currency))
            throw new BadRequestException(PaymentErrorCodes.InvalidWebhook, "PayOS payment data is invalid.");
        return new(data.orderCode, data.amount, data.reference, data.code, data.currency);
    }

    private PayOS CreateClient()
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ChecksumKey))
            throw new InternalServerException("PayOS credentials are not configured.");
        return new PayOS(settings.ClientId, settings.ApiKey, settings.ChecksumKey);
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
}
