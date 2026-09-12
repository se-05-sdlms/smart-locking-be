using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration() : BaseConfiguration<Permission>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<Permission> builder)
    {
        Varchar(builder.Property(entity => entity.Code)).IsRequired();
        Varchar(builder.Property(entity => entity.Name)).IsRequired();
        Text(builder.Property(entity => entity.Description));

        builder.HasIndex(entity => entity.Code)
            .HasDatabaseName("UX_Permission_Code")
            .IsUnique();
    }
}
