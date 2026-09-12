using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration() : BaseConfiguration<AuditLog>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<AuditLog> builder)
    {
        Varchar(builder.Property(entity => entity.Action)).IsRequired();
        Varchar(builder.Property(entity => entity.EntityType));
        EnumAsString(builder.Property(entity => entity.Result)).IsRequired();
        Varchar(builder.Property(entity => entity.IpAddress));
        Text(builder.Property(entity => entity.Details));

        builder.HasOne(entity => entity.ActorUser)
            .WithMany(user => user.AuditLogs)
            .HasForeignKey(entity => entity.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.OccurredAt)
            .HasDatabaseName("IX_AuditLog_OccurredAt");
        builder.HasIndex(entity => new { entity.ActorUserId, entity.Action, entity.OccurredAt })
            .HasDatabaseName("IX_AuditLog_Actor_Action_OccurredAt");
        builder.HasIndex(entity => new { entity.EntityType, entity.EntityId })
            .HasDatabaseName("IX_AuditLog_EntityType_EntityId");
    }
}
