using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Payments;

public sealed record OverduePaymentResponse(
    Guid Id,
    Guid OverdueChargeId,
    decimal Amount,
    string Currency,
    PaymentTransactionStatus Status,
    string? CheckoutUrl,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt);
