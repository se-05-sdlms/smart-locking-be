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
        builder.HasIndex(entity => entity.LockerId)
            .HasDatabaseName("UX_OperatorAssignment_ActiveLocker")
            .HasFilter("\"RevokedAt\" IS NULL")
            .IsUnique();
    }
}
