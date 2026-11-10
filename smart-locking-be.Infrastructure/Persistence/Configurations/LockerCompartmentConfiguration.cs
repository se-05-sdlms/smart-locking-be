using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerCompartmentConfiguration() : BaseConfiguration<LockerCompartment>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<LockerCompartment> builder)
    {
        Varchar(builder.Property(entity => entity.Code)).IsRequired().HasMaxLength(50);
        Varchar(builder.Property(entity => entity.HardwareCode)).IsRequired().HasMaxLength(100);
        builder.Property(entity => entity.HardwareChannel).IsRequired();
        EnumAsString(builder.Property(entity => entity.OperationalStatus)).IsRequired();
        EnumAsString(builder.Property(entity => entity.DoorStatus)).IsRequired();

        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.Compartments)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.LockerId, entity.Code })
            .HasDatabaseName("UX_LockerCompartment_LockerId_Code")
            .IsUnique();
        builder.HasIndex(entity => new { entity.LockerId, entity.HardwareCode })
            .HasDatabaseName("UX_LockerCompartment_LockerId_HardwareCode")
            .IsUnique();
        builder.HasIndex(entity => new { entity.LockerId, entity.HardwareChannel })
            .HasDatabaseName("UX_LockerCompartment_LockerId_HardwareChannel")
            .IsUnique();
        builder.HasIndex(entity => entity.OperationalStatus)
            .HasDatabaseName("IX_LockerCompartment_OperationalStatus");
        builder.HasIndex(entity => entity.DoorStatus)
            .HasDatabaseName("IX_LockerCompartment_DoorStatus");
    }
}
