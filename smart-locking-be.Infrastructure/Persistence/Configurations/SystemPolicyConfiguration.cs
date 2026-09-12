using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class SystemPolicyConfiguration() : BaseConfiguration<SystemPolicy>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<SystemPolicy> builder)
    {
        EnumAsString(builder.Property(entity => entity.DefaultApprovalMode)).IsRequired();
        builder.Property(entity => entity.OverdueFeePerHour).HasPrecision(18, 2);
        Varchar(builder.Property(entity => entity.Currency)).IsRequired();

        builder.HasOne(entity => entity.CreatedByUser)
            .WithMany(user => user.CreatedSystemPolicies)
            .HasForeignKey(entity => entity.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.Version)
            .HasDatabaseName("UX_SystemPolicy_Version")
            .IsUnique();
        builder.HasIndex(entity => entity.IsActive)
            .HasDatabaseName("UX_SystemPolicy_OneActive")
            .HasFilter("\"IsActive\" = TRUE")
            .IsUnique();
        builder.HasIndex(entity => entity.EffectiveFrom)
            .HasDatabaseName("IX_SystemPolicy_EffectiveFrom");

        builder.ToTable("SystemPolicy", table => table.HasCheckConstraint(
            "CK_SystemPolicy_DurationsAndRates",
            "\"DeliveryRequestExpiryMinutes\" > 0 AND " +
            "\"OverdueStartAfterHours\" >= 0 AND " +
            "\"OverdueFeePerHour\" >= 0 AND " +
            "\"MaxStorageHours\" > 0 AND " +
            "\"ClearanceEligibilityAfterHours\" >= 0 AND " +
            "\"ClearanceNoticeBeforeHours\" >= 0"));
    }
}
