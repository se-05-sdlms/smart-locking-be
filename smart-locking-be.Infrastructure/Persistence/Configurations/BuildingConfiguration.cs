using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class BuildingConfiguration() : BaseConfiguration<Building>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<Building> builder)
    {
        Varchar(builder.Property(entity => entity.Code)).IsRequired();
        Varchar(builder.Property(entity => entity.Name)).IsRequired();
        Text(builder.Property(entity => entity.Address)).IsRequired();
        EnumAsString(builder.Property(entity => entity.Status)).IsRequired();

        builder.HasIndex(entity => entity.Code)
            .HasDatabaseName("UX_Building_Code")
            .IsUnique();
        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("IX_Building_Status");
    }
}
