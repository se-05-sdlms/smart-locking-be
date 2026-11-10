namespace smart_locking_be.Application.DTOs.Payments;

public sealed record PaymentLookupQuery(string? Kind = null, string? OrderCode = null, Guid? ResidentUserId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? Status = null, int PageNumber = 1, int PageSize = 30);
public sealed record PaymentLookupResponse(string Kind, Guid Id, Guid ResidentUserId, string? OrderCode,
    decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    string? ExternalTransactionId, Guid? ReconciliationId = null);
public sealed record CreatePaymentReconciliationRequest(string Kind, Guid PaymentId, string Reason);
public sealed record RespondPaymentReconciliationRequest(string Status, string Response);
public sealed record PaymentReconciliationEventResponse(Guid Id, string Action, string Status, string Message,
    DateTimeOffset CreatedAt, Guid ActorUserId, string? ProviderEvidence);
public sealed record PaymentReconciliationResponse(Guid Id, string Reference, string Kind, Guid PaymentId,
    string Reason, string Status, Guid? AssignedAdminUserId, string? Response, string? ProviderStatus,
    DateTimeOffset? ProviderCheckedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string NextStep, IReadOnlyList<PaymentReconciliationEventResponse> History);
public sealed record PaymentGatewayInformation(long OrderCode, int Amount, int AmountPaid, int AmountRemaining,
    string Status, IReadOnlyList<PaymentGatewayTransaction> Transactions);
public sealed record PaymentGatewayTransaction(string Reference, int Amount);
