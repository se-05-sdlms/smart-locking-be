using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Infrastructure.Persistence.Configurations;

public sealed class UserRoleConfiguration() : BaseConfiguration<UserRole>(entity => entity.Id)
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserRole> builder)
    {
        builder.HasOne(entity => entity.User)
            .WithMany(user => user.UserRoles)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Role)
            .WithMany(role => role.UserRoles)
            .HasForeignKey(entity => entity.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.AssignedByUser)
            .WithMany(user => user.AssignedUserRoles)
            .HasForeignKey(entity => entity.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.UserId, entity.RoleId })
            .HasDatabaseName("UX_UserRole_UserId_RoleId")
            .IsUnique();
        builder.HasIndex(entity => entity.RoleId)
            .HasDatabaseName("IX_UserRole_RoleId");
    }
}
