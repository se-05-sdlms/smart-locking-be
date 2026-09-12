using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OperatorAssignmentConfiguration() : BaseConfiguration<OperatorAssignment>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OperatorAssignment> builder)
    {
        Text(builder.Property(entity => entity.Reason));

        builder.HasOne(entity => entity.OperatorUser)
            .WithMany(user => user.OperatorAssignments)
            .HasForeignKey(entity => entity.OperatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Building)
            .WithMany(building => building.OperatorAssignments)
            .HasForeignKey(entity => entity.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.LockerCluster)
            .WithMany(cluster => cluster.OperatorAssignments)
            .HasForeignKey(entity => entity.LockerClusterId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.OperatorAssignments)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.AssignedByUser)
            .WithMany(user => user.CreatedOperatorAssignments)
            .HasForeignKey(entity => entity.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.OperatorUserId, entity.RevokedAt })
            .HasDatabaseName("IX_OperatorAssignment_Operator_RevokedAt");
        builder.HasIndex(entity => new { entity.OperatorUserId, entity.BuildingId })
            .HasDatabaseName("UX_OperatorAssignment_ActiveBuilding")
            .HasFilter("\"RevokedAt\" IS NULL AND \"BuildingId\" IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => new { entity.OperatorUserId, entity.LockerClusterId })
            .HasDatabaseName("UX_OperatorAssignment_ActiveCluster")
            .HasFilter("\"RevokedAt\" IS NULL AND \"LockerClusterId\" IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => new { entity.OperatorUserId, entity.LockerId })
            .HasDatabaseName("UX_OperatorAssignment_ActiveLocker")
            .HasFilter("\"RevokedAt\" IS NULL AND \"LockerId\" IS NOT NULL")
            .IsUnique();

        builder.ToTable("OperatorAssignment", table => table.HasCheckConstraint(
            "CK_OperatorAssignment_ExactlyOneScope",
            "(CASE WHEN \"BuildingId\" IS NOT NULL THEN 1 ELSE 0 END + " +
            "CASE WHEN \"LockerClusterId\" IS NOT NULL THEN 1 ELSE 0 END + " +
            "CASE WHEN \"LockerId\" IS NOT NULL THEN 1 ELSE 0 END) = 1"));
    }
}
