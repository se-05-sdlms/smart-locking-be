using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class NotificationRuleConfiguration() : BaseConfiguration<NotificationRule>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<NotificationRule> builder)
    {
        Varchar(builder.Property(entity => entity.EventType)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Channel)).IsRequired();

        builder.HasOne(entity => entity.SystemPolicy)
            .WithMany(policy => policy.NotificationRules)
            .HasForeignKey(entity => entity.SystemPolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.SystemPolicyId, entity.EventType, entity.Channel })
            .HasDatabaseName("UX_NotificationRule_Policy_Event_Channel")
            .IsUnique();
        builder.HasIndex(entity => new { entity.EventType, entity.IsEnabled })
            .HasDatabaseName("IX_NotificationRule_EventType_IsEnabled");

        builder.ToTable("NotificationRule", table => table.HasCheckConstraint(
            "CK_NotificationRule_LeadTimeMinutes",
            "\"LeadTimeMinutes\" IS NULL OR \"LeadTimeMinutes\" >= 0"));
    }
}
