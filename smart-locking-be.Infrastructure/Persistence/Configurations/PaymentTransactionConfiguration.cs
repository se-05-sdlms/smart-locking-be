using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class PaymentTransactionConfiguration() : BaseConfiguration<PaymentTransaction>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentTransaction> builder)
    {
        Varchar(builder.Property(entity => entity.Provider)).IsRequired();
        Varchar(builder.Property(entity => entity.ProviderTransactionId));
        builder.Property(entity => entity.Amount).HasPrecision(18, 2);
        Varchar(builder.Property(entity => entity.Currency)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();
        Text(builder.Property(entity => entity.FailureReason));

        builder.HasOne(entity => entity.OverdueCharge)
            .WithMany(charge => charge.PaymentTransactions)
            .HasForeignKey(entity => entity.OverdueChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.Provider, entity.ProviderTransactionId })
            .HasDatabaseName("UX_PaymentTransaction_Provider_TransactionId")
            .HasFilter("\"ProviderTransactionId\" IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => new { entity.OverdueChargeId, entity.Status })
            .HasDatabaseName("IX_PaymentTransaction_Charge_Status");
        builder.HasIndex(entity => entity.RequestedAt)
            .HasDatabaseName("IX_PaymentTransaction_RequestedAt");

        builder.ToTable("PaymentTransaction", table => table.HasCheckConstraint(
            "CK_PaymentTransaction_Amount",
            "\"Amount\" > 0"));
    }
}
