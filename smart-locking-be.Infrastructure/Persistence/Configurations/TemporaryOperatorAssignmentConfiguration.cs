using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class TemporaryOperatorAssignmentConfiguration() : BaseConfiguration<TemporaryOperatorAssignment>(x => x.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<TemporaryOperatorAssignment> builder)
    {
        EnumAsString(builder.Property(x => x.Status));
        Varchar(builder.Property(x => x.StatusReason)).HasMaxLength(1000);
        Varchar(builder.Property(x => x.ServiceArea)).HasMaxLength(100);
        Varchar(builder.Property(x => x.SelectionFingerprint)).HasMaxLength(64);
        builder.HasOne(x => x.Absence).WithMany().HasForeignKey(x => x.AbsenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BaseAssignment).WithMany().HasForeignKey(x => x.BaseAssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Locker>().WithMany().HasForeignKey(x => x.LockerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OperatorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Locker>().WithMany().HasForeignKey(x => x.AnchorLockerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SystemPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AbsenceId, x.LockerId }).IsUnique();
        builder.HasIndex(x => new { x.LockerId, x.Status });
        builder.HasIndex(x => new { x.OperatorUserId, x.Status });
    }
}
