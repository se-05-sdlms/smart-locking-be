using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanConfiguration() : BaseConfiguration<SubscriptionPlan>(e => e.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        Varchar(builder.Property(e => e.Code)).IsRequired().HasMaxLength(50);
        Varchar(builder.Property(e => e.Name)).IsRequired().HasMaxLength(200);
        Text(builder.Property(e => e.Description));
        builder.Property(e => e.Price).HasPrecision(18, 2);

        builder.HasIndex(e => e.Code)
            .HasDatabaseName("UX_SubscriptionPlan_Code")
            .IsUnique();

        builder.ToTable("SubscriptionPlan", table =>
        {
            table.HasCheckConstraint("CK_SubscriptionPlan_PriceAndDuration",
                "\"Price\" >= 0 AND \"DurationDays\" > 0");
        });
    }
}
