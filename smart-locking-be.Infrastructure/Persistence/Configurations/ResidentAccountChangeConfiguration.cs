using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ResidentAccountChangeConfiguration() : BaseConfiguration<ResidentAccountChange>(e => e.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ResidentAccountChange> builder)
    {
        Varchar(builder.Property(e => e.Kind)).IsRequired().HasMaxLength(20);
        Varchar(builder.Property(e => e.Status)).IsRequired().HasMaxLength(20);
        Varchar(builder.Property(e => e.Reason)).IsRequired().HasMaxLength(2000);
        Varchar(builder.Property(e => e.ReviewReason)).HasMaxLength(2000);
        Varchar(builder.Property(e => e.OriginalPhoneNumber)).HasMaxLength(20);
        Varchar(builder.Property(e => e.NewPhoneNumber)).HasMaxLength(20);
        builder.HasOne(e => e.ResidentUser).WithMany().HasForeignKey(e => e.ResidentUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ReviewedByUser).WithMany().HasForeignKey(e => e.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.OriginalLocker).WithMany().HasForeignKey(e => e.OriginalLockerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.TargetLocker).WithMany().HasForeignKey(e => e.TargetLockerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ResidentUserId, e.CreatedAt });
        builder.HasIndex(e => new { e.ResidentUserId, e.Kind }).IsUnique().HasFilter("\"Status\" = 'Submitted'");
        builder.ToTable("ResidentAccountChange", table => table.HasCheckConstraint("CK_ResidentAccountChange_Kind",
            "(\"Kind\" = 'Locker' AND \"TargetLockerId\" IS NOT NULL AND \"NewPhoneNumber\" IS NULL) OR (\"Kind\" = 'Phone' AND \"TargetLockerId\" IS NULL AND \"NewPhoneNumber\" IS NOT NULL AND \"PhoneVerificationExpiresAt\" IS NOT NULL)"));
    }
}

public sealed class ResidentAccountChangeEventConfiguration() : BaseConfiguration<ResidentAccountChangeEvent>(e => e.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ResidentAccountChangeEvent> builder)
    {
        Varchar(builder.Property(e => e.Action)).IsRequired().HasMaxLength(40);
        Varchar(builder.Property(e => e.Message)).IsRequired().HasMaxLength(2000);
        builder.HasOne(e => e.ResidentAccountChange).WithMany(e => e.Events).HasForeignKey(e => e.ResidentAccountChangeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ActorUser).WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ResidentAccountChangeId, e.CreatedAt });
    }
}
