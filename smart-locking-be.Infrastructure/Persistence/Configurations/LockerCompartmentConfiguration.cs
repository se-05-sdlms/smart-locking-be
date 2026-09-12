using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class LockerCompartmentConfiguration() : BaseConfiguration<LockerCompartment>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<LockerCompartment> builder)
    {
        Varchar(builder.Property(entity => entity.Code)).IsRequired();
        Varchar(builder.Property(entity => entity.SizeCategory)).IsRequired();
        EnumAsString(builder.Property(entity => entity.OperationalStatus)).IsRequired();
        EnumAsString(builder.Property(entity => entity.DoorStatus)).IsRequired();

        builder.HasOne(entity => entity.Locker)
            .WithMany(locker => locker.Compartments)
            .HasForeignKey(entity => entity.LockerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.LockerId, entity.Code })
            .HasDatabaseName("UX_LockerCompartment_LockerId_Code")
            .IsUnique();
        builder.HasIndex(entity => new { entity.SizeCategory, entity.OperationalStatus })
            .HasDatabaseName("IX_LockerCompartment_Size_Operational");
        builder.HasIndex(entity => entity.DoorStatus)
            .HasDatabaseName("IX_LockerCompartment_DoorStatus");
    }
}
