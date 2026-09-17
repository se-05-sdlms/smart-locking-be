using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerConfiguration() : BaseConfiguration<Locker>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<Locker> builder)
    {
        Varchar(builder.Property(entity => entity.Code)).IsRequired().HasMaxLength(50);
        Varchar(builder.Property(entity => entity.Address)).IsRequired().HasMaxLength(500);
        Varchar(builder.Property(entity => entity.RecoveryAddress)).IsRequired().HasMaxLength(500);
        Varchar(builder.Property(entity => entity.DeviceIdentifier)).IsRequired().HasMaxLength(100);
        EnumAsString(builder.Property(entity => entity.OperationalStatus)).IsRequired();
        EnumAsString(builder.Property(entity => entity.ConnectionStatus)).IsRequired();

        builder.HasIndex(entity => entity.Code)
            .HasDatabaseName("UX_Locker_Code")
            .IsUnique();
        builder.HasIndex(entity => entity.DeviceIdentifier)
            .HasDatabaseName("UX_Locker_DeviceIdentifier")
            .IsUnique();
        builder.HasIndex(entity => new { entity.ConnectionStatus, entity.LastSeenAt })
            .HasDatabaseName("IX_Locker_ConnectionStatus_LastSeenAt");
        builder.HasIndex(entity => entity.OperationalStatus)
            .HasDatabaseName("IX_Locker_OperationalStatus");
    }
}
