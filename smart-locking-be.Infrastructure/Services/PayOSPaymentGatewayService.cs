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
            throw new ArgumentException("Thông tin tạo thanh toán PayOS không hợp lệ.", nameof(request));

        // SDK 1.x has no cancellation overload. Once dispatched, await its result so a
        // cancelled caller does not hide a payment link that the provider has created.
        var result = await client.createPaymentLink(new PaymentData(request.OrderCode, request.Amount,
            request.Description, [], request.CancelUrl, request.ReturnUrl));
        return new(result.checkoutUrl, result.paymentLinkId, result.qrCode);
    }

    public PayOSWebhookData VerifyWebhook(PayOSWebhookPayload payload)
    {
        var client = CreateClient();
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Data.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(payload.Signature))
            throw new ArgumentException("Webhook PayOS không hợp lệ.", nameof(payload));

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
            throw new ArgumentException("Chữ ký hoặc dữ liệu webhook PayOS không hợp lệ.", nameof(payload), ex);
        }

        if (data.orderCode <= 0 || data.amount <= 0 || string.IsNullOrWhiteSpace(data.reference)
            || string.IsNullOrWhiteSpace(data.code) || string.IsNullOrWhiteSpace(data.currency))
            throw new ArgumentException("Dữ liệu thanh toán PayOS không hợp lệ.", nameof(payload));
        return new(data.orderCode, data.amount, data.reference, data.code, data.currency);
    }

    private PayOS CreateClient()
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ChecksumKey))
            throw new InvalidOperationException("Chưa cấu hình thông tin xác thực PayOS.");
        return new PayOS(settings.ClientId, settings.ApiKey, settings.ChecksumKey);
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
}
