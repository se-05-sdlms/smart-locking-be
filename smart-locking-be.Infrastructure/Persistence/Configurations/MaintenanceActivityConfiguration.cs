using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class MaintenanceActivityConfiguration() : BaseConfiguration<MaintenanceActivity>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<MaintenanceActivity> builder)
    {
        Varchar(builder.Property(entity => entity.ActionType)).IsRequired();
        NullableEnumAsString(builder.Property(entity => entity.FromStatus));
        NullableEnumAsString(builder.Property(entity => entity.ToStatus));
        Text(builder.Property(entity => entity.Notes));

        builder.HasOne(entity => entity.MaintenanceRequest)
            .WithMany(request => request.Activities)
            .HasForeignKey(entity => entity.MaintenanceRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ActionByUser)
            .WithMany(user => user.MaintenanceActivities)
            .HasForeignKey(entity => entity.ActionByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.MaintenanceRequestId, entity.CreatedAt })
            .HasDatabaseName("IX_MaintenanceActivity_Request_CreatedAt");
        builder.HasIndex(entity => new { entity.ActionByUserId, entity.CreatedAt })
            .HasDatabaseName("IX_MaintenanceActivity_User_CreatedAt");
    }
}
