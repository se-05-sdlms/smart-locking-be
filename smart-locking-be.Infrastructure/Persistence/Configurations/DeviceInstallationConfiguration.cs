using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class DeviceInstallationConfiguration() : BaseConfiguration<DeviceInstallation>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<DeviceInstallation> builder)
    {
        Varchar(builder.Property(entity => entity.InstallationId)).IsRequired().HasMaxLength(100);
        Varchar(builder.Property(entity => entity.ExpoPushToken)).IsRequired().HasMaxLength(256);
        Varchar(builder.Property(entity => entity.Platform)).IsRequired().HasMaxLength(20);

        builder.HasOne(entity => entity.User)
            .WithMany(user => user.DeviceInstallations)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.InstallationId)
            .HasDatabaseName("UX_DeviceInstallation_InstallationId")
            .IsUnique();
        builder.HasIndex(entity => entity.ExpoPushToken)
            .HasDatabaseName("UX_DeviceInstallation_ExpoPushToken")
            .IsUnique();
        builder.HasIndex(entity => new { entity.UserId, entity.IsActive })
            .HasDatabaseName("IX_DeviceInstallation_UserId_IsActive");
    }
}
