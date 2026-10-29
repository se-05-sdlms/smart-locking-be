using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ResidentSubscriptionConfiguration() : BaseConfiguration<ResidentSubscription>(e => e.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ResidentSubscription> builder)
    {
        EnumAsString(builder.Property(e => e.Status)).IsRequired();
        builder.Property(e => e.PriceSnapshot).HasPrecision(18, 2);
        Varchar(builder.Property(e => e.PlanCodeSnapshot)).HasMaxLength(50);
        Varchar(builder.Property(e => e.PlanNameSnapshot)).HasMaxLength(200);
        Varchar(builder.Property(e => e.ExternalOrderCode)).HasMaxLength(100);
        Varchar(builder.Property(e => e.ExternalTransactionId)).HasMaxLength(200);

        builder.HasOne(e => e.ResidentProfile)
            .WithMany(r => r.Subscriptions)
            .HasForeignKey(e => e.ResidentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SubscriptionPlan)
            .WithMany(p => p.ResidentSubscriptions)
            .HasForeignKey(e => e.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ResidentProfileId)
            .HasDatabaseName("IX_ResidentSubscription_ResidentProfileId");

        builder.HasIndex(e => e.ExternalOrderCode)
            .HasDatabaseName("UX_ResidentSubscription_ExternalOrderCode")
            .IsUnique()
            .HasFilter("\"ExternalOrderCode\" IS NOT NULL");
    }
}
