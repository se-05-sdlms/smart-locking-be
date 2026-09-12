using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class IncidentActionConfiguration() : BaseConfiguration<IncidentAction>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<IncidentAction> builder)
    {
        Varchar(builder.Property(entity => entity.ActionType)).IsRequired();
        NullableEnumAsString(builder.Property(entity => entity.FromStatus));
        NullableEnumAsString(builder.Property(entity => entity.ToStatus));
        Text(builder.Property(entity => entity.Notes));

        builder.HasOne(entity => entity.Incident)
            .WithMany(incident => incident.Actions)
            .HasForeignKey(entity => entity.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ActionByUser)
            .WithMany(user => user.IncidentActions)
            .HasForeignKey(entity => entity.ActionByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.IncidentId, entity.CreatedAt })
            .HasDatabaseName("IX_IncidentAction_IncidentId_CreatedAt");
        builder.HasIndex(entity => new { entity.ActionByUserId, entity.CreatedAt })
            .HasDatabaseName("IX_IncidentAction_ActionByUserId_CreatedAt");
    }
}
