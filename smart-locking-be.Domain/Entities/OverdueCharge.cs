using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

public sealed class OverdueCharge
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public decimal Amount { get; set; }
    public decimal RatePerHourSnapshot { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset ChargeStartAt { get; set; }
    public DateTimeOffset CalculatedThrough { get; set; }
    public OverdueChargeStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public Parcel Parcel { get; set; } = null!;
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
}
