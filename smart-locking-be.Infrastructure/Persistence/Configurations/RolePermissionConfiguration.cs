using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionConfiguration() : BaseConfiguration<RolePermission>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasOne(entity => entity.Role)
            .WithMany(role => role.RolePermissions)
            .HasForeignKey(entity => entity.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Permission)
            .WithMany(permission => permission.RolePermissions)
            .HasForeignKey(entity => entity.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.RoleId, entity.PermissionId })
            .HasDatabaseName("UX_RolePermission_RoleId_PermissionId")
            .IsUnique();
        builder.HasIndex(entity => entity.PermissionId)
            .HasDatabaseName("IX_RolePermission_PermissionId");
    }
}
