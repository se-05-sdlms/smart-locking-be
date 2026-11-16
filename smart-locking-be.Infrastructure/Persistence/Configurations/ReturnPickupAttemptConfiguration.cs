using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ReturnPickupAttemptConfiguration : IEntityTypeConfiguration<ReturnPickupAttempt>
{
    public void Configure(EntityTypeBuilder<ReturnPickupAttempt> builder)
    {
        builder.ToTable("ReturnPickupAttempt", table => table.HasCheckConstraint(
            "CK_ReturnPickupAttempt_FailedAttempts", "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 5"));
        builder.HasKey(item => new { item.LockerId, item.ClientKey });
        builder.Property(item => item.ClientKey).HasColumnType("character varying").HasMaxLength(64);
        builder.HasOne<Locker>().WithMany().HasForeignKey(item => item.LockerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => item.WindowExpiresAt);
    }
}
