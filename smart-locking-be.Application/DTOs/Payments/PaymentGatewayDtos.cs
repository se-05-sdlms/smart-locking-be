using System.Text.Json;

namespace smart_locking_be.Application.DTOs.Payments;

public sealed record CreatePaymentLinkRequest(long OrderCode, int Amount, string Description, string ReturnUrl,
    string CancelUrl, DateTimeOffset? ExpiresAt = null);
public sealed record CreatePaymentLinkResult(string CheckoutUrl, string PaymentLinkId, string QrCode);
public sealed record PayOSWebhookData(long OrderCode, int Amount, string Reference, string Code, string Currency);
// Keep all signed fields intact for the SDK; a reduced DTO cannot verify the provider's signature.
public sealed record PayOSWebhookPayload(string Code, string Desc, bool Success, JsonElement Data, string Signature);
