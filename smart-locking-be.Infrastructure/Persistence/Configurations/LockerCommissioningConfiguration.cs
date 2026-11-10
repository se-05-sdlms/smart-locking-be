using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerCommissioningConfiguration() : BaseConfiguration<LockerCommissioning>(item => item.LockerId)
{
    protected override void ConfigureEntity(EntityTypeBuilder<LockerCommissioning> builder)
    {
        EnumAsString(builder.Property(item => item.Stage)).IsRequired().HasMaxLength(30);
        Varchar(builder.Property(item => item.BoundDeviceIdentifier)).HasMaxLength(100);
        Varchar(builder.Property(item => item.FirmwareVersion)).HasMaxLength(100);
        Varchar(builder.Property(item => item.FirmwareHardwareModel)).HasMaxLength(100);
        Varchar(builder.Property(item => item.FirmwareSha256)).HasMaxLength(64);
        Varchar(builder.Property(item => item.VerifiedConfigurationHash)).HasMaxLength(64);
        builder.Property(item => item.Revision).IsConcurrencyToken();
        builder.HasOne(item => item.Locker).WithOne().HasForeignKey<LockerCommissioning>(item => item.LockerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.VerifiedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.HandedOverByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
