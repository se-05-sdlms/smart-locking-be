using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class ResidentBiometricConfiguration() : BaseConfiguration<ResidentBiometric>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<ResidentBiometric> builder)
    {
        Varchar(builder.Property(entity => entity.TemplateReference)).IsRequired();

        builder.HasOne(entity => entity.ResidentProfile)
            .WithMany(profile => profile.Biometrics)
            .HasForeignKey(entity => entity.ResidentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.ResidentProfileId)
            .HasDatabaseName("UX_ResidentBiometric_ActiveResident")
            .HasFilter("\"RevokedAt\" IS NULL")
            .IsUnique();
        builder.HasIndex(entity => entity.TemplateReference)
            .HasDatabaseName("IX_ResidentBiometric_TemplateReference");
    }
}
