using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerClusterConfiguration() : BaseConfiguration<LockerCluster>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<LockerCluster> builder)
    {
        Varchar(builder.Property(entity => entity.Code)).IsRequired();
        Varchar(builder.Property(entity => entity.Name)).IsRequired();
        Text(builder.Property(entity => entity.LocationDescription));
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();

        builder.HasOne(entity => entity.Building)
            .WithMany(building => building.LockerClusters)
            .HasForeignKey(entity => entity.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.BuildingId, entity.Code })
            .HasDatabaseName("UX_LockerCluster_BuildingId_Code")
            .IsUnique();
        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IX_LockerCluster_Status");
    }
}
