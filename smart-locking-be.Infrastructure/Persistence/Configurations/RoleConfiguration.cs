using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration() : BaseConfiguration<Role>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<Role> builder)
    {
        Varchar(builder.Property(entity => entity.Name)).IsRequired();
        Text(builder.Property(entity => entity.Description));

        builder.HasIndex(entity => entity.Name)
            .HasDatabaseName("UX_Role_Name")
            .IsUnique();
        builder.HasIndex(entity => entity.IsActive)
            .HasDatabaseName("IX_Role_IsActive");
    }
}
