namespace smart_locking_be.Domain.Entities;

public sealed class ReturnCancellationRequest
{
    public Guid ReturnRequestId { get; set; }
    public Guid ResidentUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string Status { get; set; } = "PendingVerification";
    public DateTimeOffset? CompletedAt { get; set; }
    public ReturnRequest ReturnRequest { get; set; } = null!;
}
