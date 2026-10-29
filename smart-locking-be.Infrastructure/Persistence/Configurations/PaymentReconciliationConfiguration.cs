using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class PaymentReconciliationConfiguration() : BaseConfiguration<PaymentReconciliation>(e => e.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentReconciliation> builder)
    {
        Varchar(builder.Property(e => e.Reference)).IsRequired().HasMaxLength(40);
        Varchar(builder.Property(e => e.Status)).IsRequired().HasMaxLength(40);
        Varchar(builder.Property(e => e.ProviderStatus)).HasMaxLength(40);
        Varchar(builder.Property(e => e.Reason)).IsRequired().HasMaxLength(2000);
        Varchar(builder.Property(e => e.Response)).HasMaxLength(2000);
        builder.HasOne(e => e.ResidentUser).WithMany().HasForeignKey(e => e.ResidentUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.AssignedAdminUser).WithMany().HasForeignKey(e => e.AssignedAdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Subscription).WithMany().HasForeignKey(e => e.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.PaymentTransaction).WithMany().HasForeignKey(e => e.PaymentTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.Reference).IsUnique();
        builder.HasIndex(e => e.SubscriptionId).IsUnique().HasFilter("\"SubscriptionId\" IS NOT NULL");
        builder.HasIndex(e => e.PaymentTransactionId).IsUnique().HasFilter("\"PaymentTransactionId\" IS NOT NULL");
        builder.HasIndex(e => new { e.ResidentUserId, e.CreatedAt });
        builder.ToTable("PaymentReconciliation", t => t.HasCheckConstraint("CK_PaymentReconciliation_Subject",
            "(\"SubscriptionId\" IS NOT NULL AND \"PaymentTransactionId\" IS NULL) OR (\"SubscriptionId\" IS NULL AND \"PaymentTransactionId\" IS NOT NULL)"));
    }
}

public sealed class PaymentReconciliationEventConfiguration() : BaseConfiguration<PaymentReconciliationEvent>(e => e.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentReconciliationEvent> builder)
    {
        Varchar(builder.Property(e => e.Action)).IsRequired().HasMaxLength(40);
        Varchar(builder.Property(e => e.Status)).IsRequired().HasMaxLength(40);
        Varchar(builder.Property(e => e.Message)).IsRequired().HasMaxLength(2000);
        Text(builder.Property(e => e.ProviderEvidence));
        builder.HasOne(e => e.PaymentReconciliation).WithMany(e => e.Events).HasForeignKey(e => e.PaymentReconciliationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ActorUser).WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.PaymentReconciliationId, e.CreatedAt });
    }
}
