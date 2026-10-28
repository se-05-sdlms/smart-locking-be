using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class OperatorTaskHandoverConfiguration() : BaseConfiguration<OperatorTaskHandover>(x => x.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OperatorTaskHandover> b)
    {
        Varchar(b.Property(x => x.AuthorName)).HasMaxLength(320);
        Varchar(b.Property(x => x.TaskKind)).HasMaxLength(20);
        Varchar(b.Property(x => x.WorkDone)).HasMaxLength(2000);
        Varchar(b.Property(x => x.Blockers)).HasMaxLength(2000);
        Varchar(b.Property(x => x.NextSteps)).HasMaxLength(2000);
        b.HasOne<Locker>().WithMany().HasForeignKey(x => x.LockerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.IntendedReceiverUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TaskKind, x.TaskId, x.CreatedAt });
    }
}

public sealed class OperatorTaskAcknowledgementConfiguration() : BaseConfiguration<OperatorTaskAcknowledgement>(x => x.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<OperatorTaskAcknowledgement> b)
    {
        Varchar(b.Property(x => x.ReceiverName)).HasMaxLength(320);
        Varchar(b.Property(x => x.Notes)).HasMaxLength(1000);
        b.HasOne<OperatorTaskHandover>().WithMany().HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ReceiverUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HandoverId, x.ReceiverUserId }).IsUnique();
    }
}
